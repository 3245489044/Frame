
namespace ParameterResolver.Validation.Providers;

public interface IFunctionRegistryProvider
{
    string Name { get; }

    IReadOnlySet<string> GetFunctionNames();

    bool HasFunction(string functionName);

    IReadOnlyList<string>? GetFunctionParameterTypes(string functionName);
}
