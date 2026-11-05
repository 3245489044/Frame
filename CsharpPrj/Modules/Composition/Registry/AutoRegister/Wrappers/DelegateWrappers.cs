
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Modules.Registry.AutoRegister;

using DelegateType = System.Delegate;

namespace Modules.Registry.AutoRegister.Wrappers;

public static class DelegateWrappers
{
    public static IDelegateWrapper Passthrough => new PassthroughWrapper();

    public static IDelegateWrapper Math => new MathWrapper();

    public static IDelegateWrapper GetByName(string wrapperType)
    {
        return wrapperType?.ToLower() switch
        {
            "passthrough" or "none" or "" => Passthrough,
            "math" => Math,
            _ => Passthrough
        };
    }
}


public class PassthroughWrapper : IDelegateWrapper
{
    public string WrapperType => "passthrough";

    public DelegateType Wrap(RegisteredItemInfo item)
    {
        if (item.Executor == null)
            throw new InvalidOperationException(
                $"RegisteredItem '{item.RegisteredName}' has no Executor");

        return item.Executor;
    }
}


public class MathWrapper : IDelegateWrapper
{
    public string WrapperType => "math";

    public DelegateType Wrap(RegisteredItemInfo item)
    {
        if (item.Executor == null)
            throw new InvalidOperationException(
                $"RegisteredItem '{item.RegisteredName}' has no Executor");

        var funcName = item.RegisteredName;
        var funcParams = item.Parameters;

        return (Func<string, string>)(paramStr =>
        {
            try
            {
                var parameters = ParseMathParameters(paramStr, funcParams);
                var task = item.Executor!(parameters!);
                var resultValue = task.GetAwaiter().GetResult();
                return FormatMathResult(resultValue);
            }
            catch (Exception ex)
            {
                return $"错误: {ex.Message}";
            }
        });
    }

    private static object?[] ParseMathParameters(string paramStr,
        ExtractedMethodInfo.ParameterInfo[]? paramInfos)
    {
        if (string.IsNullOrWhiteSpace(paramStr))
            return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<object?[]>(paramStr);
            if (parsed != null)
            {
                for (int i = 0; i < parsed.Length && paramInfos != null && i < paramInfos.Length; i++)
                {
                    if (parsed[i] is JsonElement je && paramInfos[i].Type != typeof(object))
                    {
                        parsed[i] = JsonSerializer.Deserialize(je.GetRawText(), paramInfos[i].Type);
                    }
                }
                return parsed;
            }
        }
        catch { }

        var parts = paramStr.Split(',', StringSplitOptions.TrimEntries);
        var result = new object?[parts.Length];
        for (int i = 0; i < parts.Length && paramInfos != null && i < paramInfos.Length; i++)
        {
            result[i] = ConvertToType(parts[i], paramInfos[i].Type);
        }
        return result;
    }

    private static object? ConvertToType(string value, Type targetType)
    {
        if (targetType == typeof(double)) return double.Parse(value);
        if (targetType == typeof(int)) return int.Parse(value);
        if (targetType == typeof(bool)) return bool.Parse(value);
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(long)) return long.Parse(value);
        if (targetType == typeof(float)) return float.Parse(value);
        if (targetType.IsArray && value.StartsWith('['))
            return JsonSerializer.Deserialize(value, targetType);
        if (targetType.IsEnum)
            return Enum.Parse(targetType, value, ignoreCase: true);
        return Convert.ChangeType(value, targetType);
    }

    private static string FormatMathResult(object? value)
    {
        if (value == null) return "(null)";
        if (value is double d) return d.ToString("G6");
        if (value is float f) return f.ToString("G6");
        if (value is int iv) return iv.ToString();
        if (value is long l) return l.ToString();
        if (value is bool b) return b.ToString();
        if (value is double[] arr)
            return $"[{string.Join(", ", arr.Select(x => x.ToString("G6")))}]";
        if (value is int[] intArr)
            return $"[{string.Join(", ", intArr)}]";
        if (value is double[,] mat)
        {
            var sb = new StringBuilder();
            for (int row = 0; row < mat.GetLength(0); row++)
            {
                if (row > 0) sb.AppendLine();
                sb.Append(string.Join("\t",
                    Enumerable.Range(0, mat.GetLength(1)).Select(col => mat[row, col].ToString("G6"))));
            }
            return sb.ToString();
        }
        if (value is string s)
            return s;
        if (value is System.Collections.IEnumerable enumerable)
        {
            var items = enumerable.Cast<object>().Select(x => x?.ToString() ?? "null");
            return $"[{string.Join(", ", items)}]";
        }
        return value.ToString() ?? "(null)";
    }
}
