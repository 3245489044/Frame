namespace Modules.Parsing.Models;

public class MetaCommandOptions
{
    public int DefaultTimeoutMs { get; set; } = 30000;

    public int MaxRetries { get; set; } = 3;

    public bool EnableLogging { get; set; } = true;

    public bool EnableStatistics { get; set; } = true;

    public bool EnableSecurity { get; set; } = true;

    public bool EnableMiddlewarePipeline { get; set; } = true;

    public bool EnableAuthorization { get; set; } = true;

    public string? DefaultAdapterId { get; set; }

    public Dictionary<string, object> CustomServices { get; set; } = new();
}
