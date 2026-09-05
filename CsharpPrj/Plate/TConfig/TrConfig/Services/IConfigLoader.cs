
namespace TConfig;

public interface IConfigLoader : IDisposable
{
    IReadOnlyDictionary<string, object> LoadedConfigs { get; }

    int ConfigCount { get; }

    LoadResult LoadConfig(string configPath, bool showForm = false, object? uiCommandManager = null);

    bool RemoveConfig(string configName);

    void ClearConfigs();

    bool RegisterLoadedConfig(object config);

    List<string> GetConfigNames();
}

public class LoadResult
{
    public bool Success { get; set; }

    public string? ConfigName { get; set; }

    public string? ErrorMessage { get; set; }

    public object? Config { get; set; }

    public static LoadResult Ok(string? configName = null, object? config = null) => new()
    {
        Success = true,
        ConfigName = configName,
        Config = config
    };

    public static LoadResult Fail(string error, string? configName = null) => new()
    {
        Success = false,
        ErrorMessage = error,
        ConfigName = configName
    };
}
