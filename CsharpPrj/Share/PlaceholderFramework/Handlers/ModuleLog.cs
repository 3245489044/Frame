
namespace Common.PlaceholderFramework.Integrated.Handlers
{
    public static class ModuleLog
    {
        public static bool Log(string msgId, object? parameters = null, string moduleName = "WdUI", string keyPrefix = "wdui:")
            => LogPlaceholderHandlerBase.Log(msgId, moduleName, keyPrefix, parameters);

        public static Func<string, object?, bool> Create(string moduleName, string keyPrefix)
            => (msgId, parameters) => LogPlaceholderHandlerBase.Log(msgId, moduleName, keyPrefix, parameters);
    }
}
