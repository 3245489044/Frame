
using Modules.Registry.AutoRegister;

namespace Modules.Registry.AutoRegister.Description;

public interface IDescriptionProvider
{
    string SourceType { get; }

    string GetDescription(ExtractedMethodInfo method);
}

public static class DescriptionProviders
{
    public static IDescriptionProvider Default => new DescriptionFieldProvider();

    public static IDescriptionProvider None => new NoneDescriptionProvider();

    public static IDescriptionProvider GetByName(string sourceType)
    {
        return sourceType?.ToLower() switch
        {
            "xmldoc" or "xml" or "attribute" or "attr" => Default,
            "none" or "" => None,
            _ => None
        };
    }


    private class DescriptionFieldProvider : IDescriptionProvider
    {
        public string SourceType => "default";

        public string GetDescription(ExtractedMethodInfo method)
        {
            return method.Description ?? string.Empty;
        }
    }

    private class NoneDescriptionProvider : IDescriptionProvider
    {
        public string SourceType => "none";

        public string GetDescription(ExtractedMethodInfo method)
        {
            return string.Empty;
        }
    }

}
