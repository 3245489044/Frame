
namespace CommandFramework.Models;

public class LoadResult
{
    public bool IsSuccess { get; set; }

    public string? ConfigName { get; set; }

    public string? ConfigPath { get; set; }

    public string? ErrorMessage { get; set; }

    public object? ConfigObject { get; set; }

    public bool IsCached { get; set; }

    public static LoadResult Success(string configName, string configPath, object? configObject = null, bool isCached = false)
        => new()
        {
            IsSuccess = true,
            ConfigName = configName,
            ConfigPath = configPath,
            ConfigObject = configObject,
            IsCached = isCached
        };

    public static LoadResult Failure(string errorMessage, string? configPath = null)
        => new()
        {
            IsSuccess = false,
            ConfigPath = configPath,
            ErrorMessage = errorMessage
        };
}
