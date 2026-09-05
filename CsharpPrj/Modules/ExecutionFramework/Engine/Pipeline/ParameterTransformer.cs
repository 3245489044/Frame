using System.Reflection;
using Modules.Transformers;
using TConfig;

namespace ExecutionFramework.Engine;


public class ParameterTransformer : ITransformer<object?, object[]>
{
    private readonly int _count;
    private readonly string[] _paramNames;
    private readonly Func<int, object> _getDefault;
    private readonly Func<int, object?, object> _convert;
    private readonly Func<int, bool> _hasDefault;
    private readonly string _name;

    private ParameterTransformer(string name, int count,
        string[] paramNames,
        Func<int, object> getDefault,
        Func<int, object?, object> convert,
        Func<int, bool> hasDefault)
    {
        _name = name;
        _count = count;
        _paramNames = paramNames;
        _getDefault = getDefault;
        _convert = convert;
        _hasDefault = hasDefault;
    }

    public string Name => _name;

    public static ParameterTransformer ForMethod(MethodInfo method, ITypeConverter? typeConverter = null)
    {
        var methodParams = method.GetParameters();
        var names = methodParams.Select((p, idx) => p.Name ?? $"param{idx}").ToArray();
        return new ParameterTransformer(
            $"ArgsBuilder({method.Name})", methodParams.Length, names,
            i => ParameterHelper.GetParamDefault(methodParams[i]),
            (i, v) => ParameterHelper.ConvertParameter(methodParams[i], v, typeConverter),
            i => methodParams[i].HasDefaultValue);
    }

    public static ParameterTransformer ForConfig(ITrConfig config, ITypeConverter? typeConverter = null)
    {
        var methodParams = config.Parameters;
        var names = methodParams.Select((p, idx) => p.Name ?? $"param{idx}").ToArray();
        return new ParameterTransformer(
            $"ArgsBuilder({config.Name})", methodParams.Count, names,
            i => ParameterHelper.GetParamDefault(methodParams[i]),
            (i, v) => ParameterHelper.ConvertParameter(methodParams[i], v, typeConverter),
            i => methodParams[i].IsOptional);
    }

    public object[] Transform(object? parameters)
    {
        if (_count == 0)
            return Array.Empty<object>();

        var args = new object[_count];

        switch (parameters)
        {
            case null:
                FillNull(args);
                break;

            case object[] arr:
                FillFromIndexed(arr, args);
                break;

            case List<object> list:
                FillFromIndexed(list, args);
                break;

            case Dictionary<string, object> dict:
                FillFromDict(dict, args);
                break;

            default:
                FillDefault(parameters, args);
                break;
        }

        return args;
    }


    private void FillNull(object[] args)
    {
        for (int i = 0; i < _count; i++)
            args[i] = _getDefault(i);
    }

    private void FillFromIndexed(System.Collections.IList source, object[] args)
    {
        for (int i = 0; i < _count; i++)
        {
            var rawValue = i < source.Count && source[i] != null ? source[i] : _getDefault(i);
            args[i] = _convert(i, rawValue);
        }
    }

    private void FillFromDict(Dictionary<string, object> dict, object[] args)
    {
        for (int i = 0; i < _count; i++)
        {
            if (dict.TryGetValue(_paramNames[i], out var value) && value != null)
            {
                args[i] = _convert(i, value);
            }
            else if (_hasDefault(i))
            {
                // 键缺失/为 null 且形参声明了默认值（optional）→ 回填默认值（对齐参数契约"缺失项回填默认值"）；
                // 仅当形参无默认值时才抛异常，避免误伤可选参数调用方（如工作流缺 threadId → null 列全部）。
                args[i] = _getDefault(i);
            }
            else
            {
                throw new KeyNotFoundException(
                    $"参数 '{_paramNames[i]}' 在 Dictionary 中未找到。可用键: {string.Join(", ", dict.Keys)}");
            }
        }
    }

    private void FillDefault(object? parameters, object[] args)
    {
        if (_count >= 1)
            args[0] = _convert(0, parameters);
        for (int i = 1; i < _count; i++)
            args[i] = _getDefault(i);
    }
}
