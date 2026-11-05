
namespace Modules.Registry.AutoRegister.Naming;

public interface INamingConvention
{
    string Name { get; }

    string GenerateName(string className, string? nestedClassName, string methodName, NamingConfig config);
}

public static class NamingConventions
{
    public static INamingConvention NestedClass_MethodName => new NestedClassUnderscoreMethodName();

    public static INamingConvention NestedClass_DotMethodName => new NestedClassDotMethodName();

    public static INamingConvention MethodNameOnly => new PureMethodName();

    public static INamingConvention GetByName(string conventionName)
    {
        return conventionName switch
        {
            "NestedClass_MethodName" => NestedClass_MethodName,
            "NestedClass.MethodName" => NestedClass_DotMethodName,
            "MethodName" => MethodNameOnly,
            _ => MethodNameOnly
        };
    }


    private abstract class BaseNamingConvention : INamingConvention
    {
        public abstract string Name { get; }

        public string GenerateName(string className, string? nestedClassName, string methodName, NamingConfig config)
        {
            var prefix = config.Prefix ?? string.Empty;
            var suffix = config.Suffix ?? string.Empty;
            return $"{prefix}{BuildCoreName(className, nestedClassName, methodName, config)}{suffix}";
        }

        protected abstract string BuildCoreName(string className, string? nestedClassName, string methodName, NamingConfig config);
    }


    private class NestedClassUnderscoreMethodName : BaseNamingConvention
    {
        public override string Name => "NestedClass_MethodName";

        protected override string BuildCoreName(string className, string? nestedClassName, string methodName, NamingConfig config)
        {
            var separator = config.Separator ?? "_";
            return !string.IsNullOrEmpty(nestedClassName)
                ? $"{nestedClassName}{separator}{methodName}"
                : methodName;
        }
    }

    private class NestedClassDotMethodName : BaseNamingConvention
    {
        public override string Name => "NestedClass.MethodName";

        protected override string BuildCoreName(string className, string? nestedClassName, string methodName, NamingConfig config)
        {
            var separator = config.Separator ?? ".";
            return !string.IsNullOrEmpty(nestedClassName)
                ? $"{nestedClassName}{separator}{methodName}"
                : methodName;
        }
    }


    private class PureMethodName : BaseNamingConvention
    {
        public override string Name => "MethodName";

        protected override string BuildCoreName(string className, string? nestedClassName, string methodName, NamingConfig config)
        {
            return methodName;
        }
    }
}
