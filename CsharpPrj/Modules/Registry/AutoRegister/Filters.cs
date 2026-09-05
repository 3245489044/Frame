
using Modules.Registry.AutoRegister;

namespace Modules.Registry.AutoRegister.Filters;

public class MethodFilterConfig
{
    public List<string>? IncludeNestedClasses { get; set; }

    public List<string>? ExcludeNestedClasses { get; set; }

    public List<string>? ExcludeByReturnType { get; set; }

    public int? MaxParams { get; set; }

    public bool IncludeStaticOnly { get; set; }

    public List<string>? ExcludeMethodNames { get; set; }

}

public static class MethodFilter
{
    public static List<ExtractedMethodInfo> Apply(List<ExtractedMethodInfo> methods, MethodFilterConfig? config)
    {
        if (config == null)
            return methods;

        var result = methods.AsEnumerable();

        if (config.IncludeNestedClasses is { Count: > 0 })
        {
            var nestedSet = new HashSet<string>(config.IncludeNestedClasses);
            result = result.Where(m =>
                m.ContainingNestedClassName == null ||
                nestedSet.Contains(m.ContainingNestedClassName));
        }
        else if (config.ExcludeNestedClasses is { Count: > 0 })
        {
            var excludeSet = new HashSet<string>(config.ExcludeNestedClasses);
            result = result.Where(m =>
                m.ContainingNestedClassName == null ||
                !excludeSet.Contains(m.ContainingNestedClassName));
        }


        if (config.ExcludeByReturnType is { Count: > 0 })
        {
            var excludeSet = new HashSet<string>(config.ExcludeByReturnType, StringComparer.OrdinalIgnoreCase);
            result = result.Where(m => !excludeSet.Contains(m.ReturnType.Name));
        }

        if (config.MaxParams.HasValue)
        {
            result = result.Where(m => m.Parameters.Length <= config.MaxParams.Value);
        }

        if (config.IncludeStaticOnly)
        {
            result = result.Where(m => m.IsStatic);
        }

        if (config.ExcludeMethodNames is { Count: > 0 })
        {
            var excludeSet = new HashSet<string>(config.ExcludeMethodNames);
            result = result.Where(m => !excludeSet.Contains(m.MethodName));
        }

        return result.ToList();
    }
}
