
namespace ParameterResolver.Validation.Providers;

public interface IControlTreeProvider
{
    string Name { get; }

    IReadOnlySet<string> GetControlNames();

    bool HasControl(string controlName);

    string? GetControlType(string controlName);

    IReadOnlyList<string>? GetSupportedEvents(string controlName);
}
