
namespace ParameterResolver.Abstractions;

public class ParameterMatchInfo
{
    public string ParameterName { get; }

    public Type ParameterType { get; }

    public bool IsOptional { get; }

    public bool IsMatched { get; }

    public string MatchSource { get; }

    public object? RawValue { get; }

    public object? ConvertedValue { get; }

    public string? ConversionError { get; }

    public ParameterMatchInfo(
        string parameterName,
        Type parameterType,
        bool isOptional,
        bool isMatched,
        string matchSource,
        object? rawValue = null,
        object? convertedValue = null,
        string? conversionError = null)
    {
        ParameterName = parameterName;
        ParameterType = parameterType;
        IsOptional = isOptional;
        IsMatched = isMatched;
        MatchSource = matchSource;
        RawValue = rawValue;
        ConvertedValue = convertedValue;
        ConversionError = conversionError;
    }
}
