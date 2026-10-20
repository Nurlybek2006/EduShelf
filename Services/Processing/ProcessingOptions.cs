using System.ComponentModel.DataAnnotations;

namespace EduShelf.Services.Processing;

public class ExtractionOptions
{
    public const string Section = "Extraction";
    [Range(1, 1000)] public int MaxPages { get; set; } = 100;
    [Range(100, 2000000)] public int MaxCharacters { get; set; } = 200000;
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
}

public class AiOptions
{
    public const string Section = "Ai";
    [MaxLength(100)] public string Provider { get; set; } = "OpenAI";
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";
    [MaxLength(100)] public string Model { get; set; } = "gpt-4.1-mini";
    public string ApiKey { get; set; } = "";
    [MaxLength(100)] public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    [Range(1, 3072)] public int EmbeddingDimensions { get; set; } = 1536;
    public bool EnableEmbeddings { get; set; } = true;
    [Range(1, 120)] public int RequestTimeoutSeconds { get; set; } = 45;
    [Range(0, 4)] public int MaxRetries { get; set; } = 2;
    [Range(500, 16000)] public int ChunkCharacters { get; set; } = 12000;
    [Range(1, 8)] public int MaxChunks { get; set; } = 4;
    [Range(300, 4000)] public int MaxOutputTokens { get; set; } = 1800;
    public bool IsConfigured => Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) &&
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

public class ProcessingWorkerOptions
{
    public const string Section = "Worker";
    public bool Enabled { get; set; } = true;
    [Range(1, 300)] public int PollSeconds { get; set; } = 3;
    [Range(30, 3600)] public int LeaseSeconds { get; set; } = 240;
    [Range(1, 5)] public int MaxAttempts { get; set; } = 3;
    [Range(5, 1800)] public int ProcessingTimeoutSeconds { get; set; } = 180;
}

public class ProcessingException(string code, string message, bool transient = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsTransient { get; } = transient;
}
