
using TConfig;

namespace ExecutionFramework.Registry;

public static class FunctionRegistryExtensions
{
    public static void RegisterAll(this IWorkflowRegistry registry, Dictionary<string, Delegate> functions)
    {
        foreach (var (name, function) in functions)
        {
            registry.Register(name, function);
        }
    }

    public static void RegisterAll(this IWorkflowRegistry registry, Dictionary<string, (Delegate Function, string Description)> functions)
    {
        foreach (var (name, (function, description)) in functions)
        {
            registry.Register(name, function, description);
        }
    }

    public static object? TryCall(this IWorkflowRegistry registry, string name, params object?[] args)
    {
        try
        {
            return registry.Call(name, args);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<object?> TryCallAsync(this IWorkflowRegistry registry, string name, params object?[] args)
    {
        try
        {
            return await registry.CallAsync(name, args).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static OverridableFunctionRegistry AsOverridable(this IWorkflowRegistry registry)
    {
        if (registry is OverridableFunctionRegistry overridable)
            return overridable;
        return new OverridableFunctionRegistry(registry);
    }

    public static int Count(this IWorkflowRegistry registry)
        => registry.GetFunctionNames().Count();
}
