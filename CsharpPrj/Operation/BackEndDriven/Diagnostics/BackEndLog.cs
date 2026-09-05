#nullable enable
using BackEndDriven.Diagnostics;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace BackEndDriven.Diagnostics
{
    public static class BackEndLog
    {
        private const string ModuleName = "BackEndDriven";
        private const string KeyPrefix = "backend:";

        public static void Initialize(string jsonFilePath)
        {
            LogPlaceholderHandlerBase.Initialize(jsonFilePath, KeyPrefix);
        }

        public static void Log(string messageId, object? data = null)
        {
            LogBase.Log(ModuleName, KeyPrefix, messageId, data);
        }

        public static void Log(string messageId, string message)
        {
            LogBase.Log(ModuleName, KeyPrefix, messageId, message);
        }
    }
}
