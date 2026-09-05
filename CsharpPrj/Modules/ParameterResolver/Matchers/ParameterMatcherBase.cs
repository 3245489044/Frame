
using ParameterResolver.Abstractions;
using ParameterResolver.Converter;

namespace ParameterResolver.Matchers;

internal abstract class ParameterMatcherBase : IParameterMatcher
{
    public abstract void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos);

    protected static void SetMatch(
        int index, IParameterDescriptor param, object? rawValue, object? convertedValue,
        string matchSource, object?[] args, bool[] matchedFlags, ParameterMatchInfo[] matchInfos)
    {
        args[index] = convertedValue;
        matchedFlags[index] = true;
        matchInfos[index] = new ParameterMatchInfo(
            param.Name, param.Type, param.IsOptional,
            isMatched: true, matchSource: matchSource,
            rawValue: rawValue, convertedValue: convertedValue,
            conversionError: null);
    }

    protected static void ConvertAndSetMatch(
        int index, IParameterDescriptor param, object? rawValue,
        string matchSource, object?[] args, bool[] matchedFlags, ParameterMatchInfo[] matchInfos)
    {
        var (converted, error) = Converter.TypeConverter.Convert(rawValue, param.Type);

        if (error != null)
        {
            args[index] = null;
            matchedFlags[index] = false;
            matchInfos[index] = new ParameterMatchInfo(
                param.Name, param.Type, param.IsOptional,
                isMatched: false, matchSource: matchSource,
                rawValue: rawValue, convertedValue: null,
                conversionError: error);
            return;
        }

        args[index] = converted;
        matchedFlags[index] = true;
        matchInfos[index] = new ParameterMatchInfo(
            param.Name, param.Type, param.IsOptional,
            isMatched: true, matchSource: matchSource,
            rawValue: rawValue, convertedValue: converted,
            conversionError: null);
    }
}
