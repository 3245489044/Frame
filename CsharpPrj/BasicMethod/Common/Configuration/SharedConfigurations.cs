using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Common.RemoteProcess.Configuration
{
    public class IpcConfiguration
    {
        public int MessageLengthPrefixSize { get; set; } = 4;
        public int DefaultReceiveBufferSize { get; set; } = 65536;
        public int MaxMessageSizeMultiplier { get; set; } = 10;
        public int DefaultRequestTimeout { get; set; } = 30000;
        public int ConnectTimeout { get; set; } = 5000;
        public string MethodIdentifierSeparator { get; set; } = "|";
        public int ListenerRetryDelayMs { get; set; } = 1000;
        public double ListenerRetryBackoffFactor { get; set; } = 1.5;
        public int ListenerMaxRetryDelayMs { get; set; } = 30000;
    }
}

namespace Common.GraphMindStudio
{
}

namespace Common.DynamicMethodLookup
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class DynamicMethodAttribute : Attribute
    {
        public string Name { get; }
        public int Order { get; set; }
    }

    public static class MethodFinder
    {
        public static List<MethodInfo> FindDynamicMethods(Assembly assembly, string methodName = null)
        {
            var methods = new List<MethodInfo>();

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.Instance | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<DynamicMethodAttribute>();
                    if (attr != null)
                    {
                        if (methodName == null || attr.Name == methodName)
                        {
                            methods.Add(method);
                        }
                    }
                }
            }

            return methods.OrderBy(m => m.GetCustomAttribute<DynamicMethodAttribute>()?.Order ?? 0).ToList();
        }
    }
}
