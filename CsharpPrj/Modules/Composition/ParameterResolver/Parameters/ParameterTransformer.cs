using System.Diagnostics;
using System.Reflection;
using Modules.Transformers;
using TConfig;

namespace ParameterResolver.Parameters;


public class ParameterTransformer : ITransformer<object?, object[]>
{
    private readonly int _count;
    private readonly string[] _paramNames;
    private readonly Func<int, object> _getDefault;
    private readonly Func<int, object?, object> _convert;
    private readonly string _name;

    private ParameterTransformer(string name, int count,
        string[] paramNames,
        Func<int, object> getDefault,
        Func<int, object?, object> convert)
    {
        _name = name;
        _count = count;
        _paramNames = paramNames;
        _getDefault = getDefault;
        _convert = convert;
    }

    public string Name => _name;

    public static ParameterTransformer ForMethod(MethodInfo method, ITypeConverter? typeConverter = null)
    {
        var methodParams = method.GetParameters();
        var names = methodParams.Select((p, idx) => p.Name ?? $"param{idx}").ToArray();
        return new ParameterTransformer(
            $"ArgsBuilder({method.Name})", methodParams.Length, names,
            i => ParameterHelper.GetParamDefault(methodParams[i]),
            (i, v) => ParameterHelper.ConvertParameter(methodParams[i], v, typeConverter));
    }

    public static ParameterTransformer ForConfig(ITrConfig config, ITypeConverter? typeConverter = null)
    {
        var methodParams = config.Parameters;
        var names = methodParams.Select((p, idx) => p.Name ?? $"param{idx}").ToArray();
        return new ParameterTransformer(
            $"ArgsBuilder({config.Name})", methodParams.Count, names,
            i => ParameterHelper.GetParamDefault(methodParams[i]),
            (i, v) => ParameterHelper.ConvertParameter(methodParams[i], v, typeConverter));
    }

    public object[] Transform(object? parameters)
    {
        if (_count == 0)
            return Array.Empty<object>();

        var args = new object[_count];

        var dispatcher = BuildDispatcher();
        dispatcher.Transform((parameters, args));
        return args;
    }

    private ITransformer<(object? parameters, object[] args), object[]> BuildDispatcher()
    {
        var nullCase = new Func<(object? parameters, object[] args), object[]>(FillNull)
            .ToTransformer("NullCase");
        var listCase = new Func<(object? parameters, object[] args), object[]>(FillFromList)
            .ToTransformer("ListCase");
        var dictCase = new Func<(object? parameters, object[] args), object[]>(FillFromDict)
            .ToTransformer("DictCase");
        var defaultCase = new Func<(object? parameters, object[] args), object[]>(FillDefault)
            .ToTransformer("DefaultCase");

        var nonDict = new ConditionalTransformer<(object? parameters, object[] args), object[]>(
            "NonDict",
            tuple => tuple.parameters is Dictionary<string, object>,
            dictCase, defaultCase);

        var nonList = new ConditionalTransformer<(object? parameters, object[] args), object[]>(
            "NonList",
            tuple => tuple.parameters is System.Collections.IList,
            listCase, nonDict);

        return new ConditionalTransformer<(object? parameters, object[] args), object[]>(
            $"{Name}.Dispatcher",
            tuple => tuple.parameters == null,
            nullCase, nonList);
    }


    private object[] FillNull((object? parameters, object[] args) tuple)
    {
        for (int i = 0; i < _count; i++)
            tuple.args[i] = _getDefault(i);
        return tuple.args;
    }

    private object[] FillFromList((object? parameters, object[] args) tuple)
        => FillFromIndexed((System.Collections.IList)tuple.parameters!, tuple.args);

    private object[] FillFromIndexed(System.Collections.IList source, object[] args)
    {
        for (int i = 0; i < _count; i++)
        {
            var rawValue = i < source.Count && source[i] != null ? source[i] : _getDefault(i);
            args[i] = _convert(i, rawValue);
        }
        return args;
    }

    private object[] FillFromDict((object? parameters, object[] args) tuple)
    {
        var dict = (Dictionary<string, object>)tuple.parameters!;
        for (int i = 0; i < _count; i++)
        {
            if (dict.TryGetValue(_paramNames[i], out var value) && value != null)
            {
                tuple.args[i] = _convert(i, value);
            }
            else
            {
                Debug.WriteLine($"[{_name}] Warning: Parameter '{_paramNames[i]}' not found in dictionary, using default value.");
                tuple.args[i] = _getDefault(i);
            }
        }
        return tuple.args;
    }

    private object[] FillDefault((object? parameters, object[] args) tuple)
    {
        if (_count >= 1)
            tuple.args[0] = _convert(0, tuple.parameters);
        for (int i = 1; i < _count; i++)
            tuple.args[i] = _getDefault(i);
        return tuple.args;
    }
}
