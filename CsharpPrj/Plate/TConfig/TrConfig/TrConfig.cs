
namespace TConfig
{
    public static class TrConfig
    {
        public static bool IsAsyncType(Type returnType)
        {
            if (returnType == null)
                return false;

            if (returnType == typeof(Task) || returnType == typeof(ValueTask))
                return true;

            if (returnType.IsGenericType)
            {
                var genericDef = returnType.GetGenericTypeDefinition();
                if (genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>))
                    return true;
            }

            return false;
        }
    }
}
