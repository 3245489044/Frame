
namespace TConfig;

public interface ILogService
{
    void Log(string level, string category, string module, string message);

    void Info(string category, string module, string message);

    void Warn(string category, string module, string message);

    void Error(string category, string module, string message);

    void Debug(string category, string module, string message);

    void LogByKey(string messageKey, object? data = null);

    int Count { get; }

    void Clear();
}

public static class LogCategories
{
    public const string WdUI_Layout = "WdUI.Layout";
    public const string WdUI_Render = "WdUI.Render";
    public const string FileDriven_Command = "FileDriven.Command";
    public const string FileDriven_Execution = "FileDriven.Execution";
    public const string FileDriven_Pipeline = "FileDriven.Pipeline";
    public const string FileDriven_Inspector = "FileDriven.Inspector";
    public const string FileDriven_Logging = "FileDriven.Logging";
    public const string FileDriven_Lifecycle = "FileDriven.Lifecycle";
    public const string Common_Diagnostics = "Common.Diagnostics";
    public const string All = "All";
}

public enum LogOutputMode
{
    Console = 0,
    Database = 1
}
