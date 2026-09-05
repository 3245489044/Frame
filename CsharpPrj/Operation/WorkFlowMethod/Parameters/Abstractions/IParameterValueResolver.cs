
using ParameterBinding = TConfig.ParameterBinding;

namespace WorkFlowMethod.Parameters.Abstractions;

public interface IParameterValueResolver
{
    object? ResolveValue(object? parentForm, ParameterBinding binding);

    object? ResolveControlValue(object? form, ParameterBinding binding);

    object? ResolveLiteralValue(string source);

    object? ResolveExpressionValue(string expression, IDictionary<string, object>? variables = null);

    string? ReadListViewSelectedValue(object listView);
}
