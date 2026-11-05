
namespace ParameterResolver.Abstractions;

public interface IParameterSource
{
    string? ReadValue(string controlName);
}
