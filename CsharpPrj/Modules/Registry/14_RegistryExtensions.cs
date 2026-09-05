
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Modules.Registry;

public static class FunctionRegistryExtensions
{
    public static FunctionRegistry RegisterAction(this FunctionRegistry registry, string name, Action action, string? description = null)
    {
        registry.Register(name, action, description);
        return registry;
    }

    public static FunctionRegistry RegisterAction<T>(this FunctionRegistry registry, string name, Action<T> action, string? description = null)
    {
        registry.Register(name, action, description);
        return registry;
    }

    public static FunctionRegistry RegisterFunc<TResult>(this FunctionRegistry registry, string name, Func<TResult> func, string? description = null)
    {
        registry.Register(name, func, description);
        return registry;
    }

    public static FunctionRegistry RegisterFunc<T, TResult>(this FunctionRegistry registry, string name, Func<T, TResult> func, string? description = null)
    {
        registry.Register(name, func, description);
        return registry;
    }

    public static FunctionRegistry RegisterFunc<T1, T2, TResult>(this FunctionRegistry registry, string name, Func<T1, T2, TResult> func, string? description = null)
    {
        registry.Register(name, func, description);
        return registry;
    }

    public static FunctionRegistry RegisterAsyncFunc<TResult>(this FunctionRegistry registry, string name, Func<Task<TResult>> func, string? description = null)
    {
        registry.Register(name, func, description);
        return registry;
    }

    public static FunctionRegistry RegisterAsyncFunc<T, TResult>(this FunctionRegistry registry, string name, Func<T, Task<TResult>> func, string? description = null)
    {
        registry.Register(name, func, description);
        return registry;
    }

    public static FunctionRegistry RegisterClass<T>(this FunctionRegistry registry, string? prefix = null) where T : class
    {
        var type = typeof(T);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

        foreach (var method in methods)
        {
            if (method.IsSpecialName) continue;

            var name = string.IsNullOrEmpty(prefix) ? method.Name : $"{prefix}.{method.Name}";
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;

            try
            {
                var del = Delegate.CreateDelegate(GetDelegateType(method), method);
                registry.Register(name, del, description);
            }
            catch
            {
            }
        }

        return registry;
    }

    private static Type GetDelegateType(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var paramTypes = parameters.Select(p => p.ParameterType).ToArray();

        if (method.ReturnType == typeof(void))
        {
            return Expression.GetDelegateType(paramTypes.Append(typeof(void)).ToArray());
        }
        else
        {
            return Expression.GetDelegateType(paramTypes.Append(method.ReturnType).ToArray());
        }
    }
}
