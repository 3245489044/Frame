
namespace TConfig;

public interface IEventHub
{
    event EventHandler? InitializationCompleted;

    event EventHandler<ConfigLoadedEventArgs>? ConfigLoaded;

    void RaiseInitializationCompleted();

    void RaiseConfigLoaded(string configName, bool success);

    void ClearAllSubscriptions();
}

public class ConfigLoadedEventArgs : EventArgs
{
    public string ConfigName { get; }

    public bool Success { get; }

    public ConfigLoadedEventArgs(string configName, bool success)
    {
        ConfigName = configName;
        Success = success;
    }
}
