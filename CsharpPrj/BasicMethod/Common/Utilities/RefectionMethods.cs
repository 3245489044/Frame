namespace Common.GraphMindStudio
{
#nullable disable
    using System;
    using System.Reflection;
    using System.Text;
    using System.Diagnostics;

    public static class RefectionMethods
    {
        public static string GetCallerInfo(int callerLevel, Func<StackFrame, MethodBase, string> selector)
        {
            StackTrace stackTrace = new(true);
            StackFrame[] stackFrames = stackTrace.GetFrames();

            if (stackFrames != null && stackFrames.Length > callerLevel)
            {
                StackFrame callerFrame = stackFrames[callerLevel];
                MethodBase methodBase = callerFrame.GetMethod();

                return selector(callerFrame, methodBase);
            }

            return string.Empty;
        }

        public static string GetCurrentMethodName(int i)
        {
            return GetCallerInfo(i, (frame, method) => method.Name);
        }

        public static string GetCurrentMethodClassName(int i)
        {
            return GetCallerInfo(i, (frame, method) => method.DeclaringType?.FullName ?? string.Empty);
        }

        public static Func<object> GetdicValueDel(Type type, string propertyName, BindingFlags bindingFlags, Action<object> action = null)
        {
            return () =>
            {
                var storagepropertyobj = type.GetProperty(propertyName, bindingFlags);
                var parameters = storagepropertyobj.GetValue(null);
                action(parameters);
                return parameters;
            };
        }

        public static void GetValueDel(MethodInfo method, object obj)
        {
            method.Invoke(obj, null);
        }

        public static string PrintCallChain()
        {
            StackTrace stackTrace = new StackTrace();
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < stackTrace.FrameCount; i++)
            {
                var method = stackTrace.GetFrame(i).GetMethod();
                sb.AppendLine($"{i}: {method.DeclaringType.FullName}.{method.Name}");
            }
            return sb.ToString();
        }
    }
}