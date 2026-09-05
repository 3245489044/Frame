namespace Rge.Unified.Resolution;

using Rge.Unified.Models;

public readonly record struct ResolveResult
{
    public bool Success { get; init; }

    public object? Value { get; init; }

    public string? Error { get; init; }

    public static ResolveResult Ok(object? value) => new() { Success = true, Value = value };
    public static ResolveResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed class ReferenceResolver
{
    public ResolveResult Resolve(ExecutionGraph graph, Entity current, string path)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(path))
            return ResolveResult.Fail("引用路径为空");

        if (path.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            return ResolveEnvironment(path["$env:".Length..]);
        if (path.StartsWith("$const:", StringComparison.OrdinalIgnoreCase))
            return ResolveResult.Ok(ParseConstant(path["$const:".Length..]));

        if (path.StartsWith("../", StringComparison.OrdinalIgnoreCase))
        {
            var parentGraph = graph.Parent;
            var remainder = path[3..];
            while (parentGraph != null && remainder.StartsWith("../", StringComparison.OrdinalIgnoreCase))
            {
                parentGraph = parentGraph.Parent;
                remainder = remainder[3..];
            }
            return parentGraph != null
                ? ResolveWithinScope(parentGraph, remainder)
                : ResolveResult.Fail($"父图引用 '{path}' 越界（当前图已是根图）");
        }

        var scopePath = path.StartsWith("./", StringComparison.OrdinalIgnoreCase) ? path[2..] : path;
        return ResolveWithinScope(graph, scopePath);
    }

    public ResolveResult ResolveParamRef(ExecutionGraph graph, Entity current, ParamRef paramRef)
    {
        var result = Resolve(graph, current, paramRef.Path);
        if (result.Success)
            return result;

        if (paramRef.Fallback != null)
            return ResolveResult.Ok(paramRef.Fallback);

        return result;
    }

    public Dictionary<string, object?> ResolveAll(ExecutionGraph graph, Entity current, out List<string> errors)
    {
        var parameters = new Dictionary<string, object?>();
        errors = new List<string>();

        foreach (var paramRef in current.ParamRefs)
        {
            var result = ResolveParamRef(graph, current, paramRef);
            if (result.Success)
            {
                parameters[paramRef.Target] = result.Value;
            }
            else
            {
                if (paramRef.Required)
                    errors.Add($"参数 '{paramRef.Target}' 引用路径 '{paramRef.Path}' 解析失败: {result.Error}");
            }
        }

        return parameters;
    }


    private static ResolveResult ResolveEnvironment(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return value != null
            ? ResolveResult.Ok(value)
            : ResolveResult.Fail($"环境变量 '{key}' 不存在");
    }

    private static ResolveResult ResolveWithinScope(ExecutionGraph scope, string path)
    {
        if (path.StartsWith("input", StringComparison.OrdinalIgnoreCase))
        {
            var afterInput = path["input".Length..];
            if (afterInput.Length == 0)
                return ResolveResult.Ok(scope.RootInput);
            if (afterInput[0] == '.' || afterInput[0] == '/')
                return ResolvePropertyPath(scope.RootInput, afterInput[1..]);
        }

        var segments = path.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return ResolveResult.Fail($"无效路径 '{path}'");

        var uid = segments[0];
        var entity = scope.Find(uid);
        if (entity == null)
            return ResolveResult.Fail($"图 '{scope.GraphId}' 中不存在实体 '{uid}'");

        if (segments.Length == 1)
            return ResolveResult.Ok(entity.Value);

        var outputKey = segments[1];
        if (entity.Outputs.TryGetValue(outputKey, out var value))
            return ResolveResult.Ok(value);

        return ResolveResult.Fail($"实体 '{uid}' 没有输出键 '{outputKey}'");
    }

    private static object? ParseConstant(string literal)
    {
        if (bool.TryParse(literal, out var b)) return b;
        if (int.TryParse(literal, out var i)) return i;
        if (double.TryParse(literal, out var d)) return d;
        if (string.Equals(literal, "null", StringComparison.OrdinalIgnoreCase)) return null;
        return literal;
    }

    private static ResolveResult ResolvePropertyPath(object? root, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        object? current = root;

        foreach (var segment in segments)
        {
            current = current switch
            {
                System.Collections.IDictionary dict =>
                    dict.Contains(segment) ? dict[segment] : MissingValue.Instance,
                System.Collections.Generic.IReadOnlyDictionary<string, object?> roDict =>
                    roDict.TryGetValue(segment, out var v) ? v : MissingValue.Instance,
                _ => TryReflectProperty(current, segment)
            };

            if (ReferenceEquals(current, MissingValue.Instance))
                return ResolveResult.Fail($"属性路径 '{path}' 在段 '{segment}' 中断");
        }

        return ResolveResult.Ok(current);
    }

    private static object? TryReflectProperty(object? target, string name)
    {
        if (target == null)
            return MissingValue.Instance;
        var prop = target.GetType().GetProperty(name);
        return prop?.GetValue(target) ?? MissingValue.Instance;
    }

    private sealed class MissingValue
    {
        public static readonly MissingValue Instance = new();
        private MissingValue() { }
    }
}