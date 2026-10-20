using Microsoft.Extensions.Options;

namespace EduShelf.Services.Processing;

public sealed class ProcessingWorker(IServiceScopeFactory scopes, IOptions<ProcessingWorkerOptions> options, ILogger<ProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Each iteration owns a fresh DbContext and disposes it after one job.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ResourceProcessor>().ProcessNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Background processing unavailable ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public static class ProcessingRegistration
{
    public static IServiceCollection AddResourceProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExtractionOptions>().Bind(configuration.GetSection(ExtractionOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ProcessingWorkerOptions>().Bind(configuration.GetSection(ProcessingWorkerOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient<IOpenAiAnalysisClient, OpenAiAnalysisClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IPdfTextExtractor, PdfTextExtractor>();
        services.AddScoped<IResourceTextExtractor, PdfTextExtractor>();
        services.AddScoped<ProcessingQueue>();
        services.AddScoped<IProcessingQueue>(provider => provider.GetRequiredService<ProcessingQueue>());
        services.AddScoped<IResourceAnalysisService, QueuedResourceAnalysisService>();
        services.AddScoped<ResourceProcessor>();
        services.AddHostedService<ProcessingWorker>();
        return services;
    }
}
