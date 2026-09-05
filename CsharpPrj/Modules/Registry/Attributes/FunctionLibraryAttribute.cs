
namespace Modules.Registry;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class FunctionLibraryAttribute : Attribute
{
    public string? SourceName { get; }

    public bool AutoDiscover { get; set; } = true;

    public FunctionLibraryAttribute(string? sourceName = null)
    {
        SourceName = sourceName;
    }
}