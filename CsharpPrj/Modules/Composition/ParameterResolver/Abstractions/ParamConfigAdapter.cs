
namespace ParameterResolver.Abstractions;

public sealed class ParamConfigAdapter : IParameterDescriptor
{
    private readonly string _name;
    private readonly Type _type;
    private readonly bool _isOptional;
    private readonly object? _defaultValue;

    public ParamConfigAdapter(string name, Type type, bool isOptional, object? defaultValue)
    {
        _name = name;
        _type = type;
        _isOptional = isOptional;
        _defaultValue = defaultValue;
    }

    public string Name => _name;

    public Type Type => _type;

    public bool IsOptional => _isOptional;

    public object? DefaultValue => _defaultValue;
}