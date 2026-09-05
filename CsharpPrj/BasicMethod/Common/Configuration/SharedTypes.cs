#nullable disable   
namespace Common
{
    public static class LocalPath
    {
        public static Func<string, string> StreamingPath = (filePath) => filePath;
    }

    public interface IDebugPrint
    {
        void Print(object message, bool useSerialize = false, string tag = null);
    }

    public static class DebugPrintProvider
    {
        private static IDebugPrint _instance;
        public static IDebugPrint Instance
        {
            get => _instance ?? new DefaultDebugPrint();
            set => _instance = value;
        }

        public static void DebugPrint(object message, bool useSerialize = false, string tag = null)
        {
            Instance.Print(message, useSerialize, tag);
        }
    }

    internal class DefaultDebugPrint : IDebugPrint
    {
        public void Print(object message, bool useSerialize = false, string tag = null)
        {
        }

    }
}