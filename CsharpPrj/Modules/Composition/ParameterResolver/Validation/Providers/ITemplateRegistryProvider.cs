
namespace ParameterResolver.Validation.Providers;

public interface ITemplateRegistryProvider
{
    string Name { get; }

    IReadOnlySet<string> GetTemplateNames();

    bool HasTemplate(string templateName);

    string? GetTemplateDescription(string templateName);
}
