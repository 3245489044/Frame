
using Common.PlaceholderFramework.Integrated.Handlers;

public static class FsLog
{
    private static readonly Func<string, object?, bool> _log = ModuleLog.Create("FunctionStorage", "fs:");

    public static bool Log(string msgId, object? parameters = null)
        => _log(msgId, parameters);
}