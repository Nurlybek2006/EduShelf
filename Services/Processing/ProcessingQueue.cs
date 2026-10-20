using System.Data;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace EduShelf.Services.Processing;

public interface IProcessingQueue
{
    Task EnqueueAsync(int resourceId, bool analyze, CancellationToken cancellationToken = default);
}
public sealed record ProcessingLease(int ResourceId, string FileHash, bool AnalyzeRequested, Guid Token, int Attempt);

public sealed class ProcessingQueue(ApplicationDbContext db, IOptions<AiOptions> aiOptions, IOptions<ProcessingWorkerOptions> workerOptions) : IProcessingQueue
{
    public async Task EnqueueAsync(int resourceId, bool analyze, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Serialize by the existing resource row, including the first queue insert. Its PK also guarantees one durable job per resource.
        var resource = await db.Resources.FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {resourceId}")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException();
        var job = await db.ProcessingJobs.SingleOrDefaultAsync(x => x.ResourceId == resourceId, cancellationToken);
        var now = DateTime.UtcNow;
        var active = job is not null && job.FileHash == resource.FileHash && job.Status is JobStatus.Pending or JobStatus.Processing;
        if (job is null) { job = new ProcessingJob { ResourceId = resourceId, CreatedAtUtc = now }; db.ProcessingJobs.Add(job); }
        if (!active)
        {
            job.FileHash = resource.FileHash;
            job.Status = JobStatus.Pending;
            job.Attempts = 0;
            job.LeaseToken = null;
            job.LeaseExpiresAtUtc = null;
            job.NotBeforeUtc = now;
            job.LastErrorCode = null;
            job.AnalyzeRequested = analyze;
        }
        else job.AnalyzeRequested |= analyze;
        job.UpdatedAtUtc = now;
        if (analyze)
        {
            var analysis = await db.ResourceAnalyses.SingleOrDefaultAsync(x => x.ResourceId == resourceId, cancellationToken);
            if (analysis is null) { analysis = new ResourceAnalysis { ResourceId = resourceId }; db.ResourceAnalyses.Add(analysis); }
            if (!active || analysis.Status is not AnalysisStatus.Processing)
                analysis.Status = aiOptions.Value.IsConfigured ? AnalysisStatus.Pending : AnalysisStatus.NotConfigured;
            analysis.ErrorCode = aiOptions.Value.IsConfigured ? null : "AiNotConfigured";
            analysis.ErrorMessage = aiOptions.Value.IsConfigured ? null : "AI қызметі бапталмаған.";
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProcessingLease?> TryClaimAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var token = Guid.NewGuid();
        var expires = now.AddSeconds(Math.Max(workerOptions.Value.LeaseSeconds, workerOptions.Value.ProcessingTimeoutSeconds + 15));
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            command.CommandText = """
            UPDATE [ProcessingJobs] SET [Status]='Failed', [LastErrorCode]='AttemptsExhausted', [LeaseToken]=NULL, [LeaseExpiresAtUtc]=NULL, [UpdatedAtUtc]=@now
            WHERE [Status]='Processing' AND [LeaseExpiresAtUtc] <= @now AND [Attempts] >= @maximum;
            UPDATE a SET [Status]='Failed', [ErrorCode]='AttemptsExhausted', [ErrorMessage]=N'Өңдеудің қайталау шегі аяқталды. Қайта іске қосыңыз.'
            FROM [ResourceAnalyses] a JOIN [ProcessingJobs] j ON a.[ResourceId]=j.[ResourceId]
            WHERE j.[Status]='Failed' AND j.[LastErrorCode]='AttemptsExhausted' AND a.[Status] IN ('Pending','Processing');
            UPDATE e SET [Status]='Failed', [ErrorCode]='AttemptsExhausted', [ErrorMessage]=N'Мәтін алудың қайталау шегі аяқталды. Қайта іске қосыңыз.'
            FROM [ResourceTextExtractions] e JOIN [ProcessingJobs] j ON e.[ResourceId]=j.[ResourceId]
            JOIN [Resources] r ON r.[Id]=j.[ResourceId] AND r.[FileHash]=j.[FileHash]
            WHERE j.[Status]='Failed' AND j.[LastErrorCode]='AttemptsExhausted' AND e.[Status] IN ('Pending','Processing');
            ;WITH candidate AS (
              SELECT TOP (1) * FROM [ProcessingJobs] WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
              WHERE ([Status]='Pending' AND [NotBeforeUtc] <= @now OR [Status]='Processing' AND [LeaseExpiresAtUtc] <= @now)
                AND [Attempts] < @maximum
              ORDER BY [NotBeforeUtc], [ResourceId]
            )
            UPDATE candidate SET [Status]='Processing', [Attempts]=[Attempts]+1, [LeaseToken]=@token,
              [LeaseExpiresAtUtc]=@expires, [UpdatedAtUtc]=@now
            OUTPUT inserted.[ResourceId], inserted.[FileHash], inserted.[AnalyzeRequested], inserted.[Attempts];
            """;
            Add("@now", now); Add("@maximum", workerOptions.Value.MaxAttempts); Add("@token", token); Add("@expires", expires);
            void Add(string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), token, reader.GetInt32(3)) : null;
        }
        finally { if (opened) await connection.CloseAsync(); }
    }
}
