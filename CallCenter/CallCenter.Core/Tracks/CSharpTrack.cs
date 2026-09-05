using System.Reflection;
using System.Text.Json;
using CallCenter.Core.Discovery;
using CallCenter.Core.Tools;

namespace CallCenter.Core.Tracks;

/// <summary>
/// C#Track（Runtime 轨）：对 <see cref="CSharpFunctionLibrarySource.DiscoverDefinitions"/> 产出的函数库定义
/// 建索引，按名以 JSON 入参真实执行委托并把结果序列化为 JSON 返回。只出现在终端面；控制面/上层以 JSON 交互，
/// 不感知 C# 类型。无委托（仅 config 描述）的工具不可执行，<see cref="CanCall"/> 返回 false。
/// </summary>
public sealed class CSharpTrack : ICallableTrack
{
    private readonly IReadOnlyList<ToolDescriptor> _tools;
    private readonly IReadOnlyDictionary<string, Delegate> _calls;
    private readonly ICSharpDelegateInvoker _invoker;

    /// <summary>缺省执行后端 = <see cref="DirectDelegateInvoker"/>（零回归）。</summary>
    public CSharpTrack(IEnumerable<CSharpToolDefinition> definitions)
        : this(definitions, null)
    {
    }

    /// <summary>
    /// 以自定义执行后端构造（宿主注入统一执行栈，如 ExecutionFramework.FunctionExecutor）；为 null 时回落
    /// <see cref="DirectDelegateInvoker"/>。委托发现/入参绑定仍在本轨完成，后端只负责真实调用与结果映射。
    /// </summary>
    public CSharpTrack(IEnumerable<CSharpToolDefinition> definitions, ICSharpDelegateInvoker? invoker)
    {
        var defs = definitions?.ToArray() ?? Array.Empty<CSharpToolDefinition>();

        // 单遍填充 _tools/_calls（A4）：同一 defs 只过滤一次，不重复 Where(Delegate != null)。
        var tools = new List<ToolDescriptor>();
        var calls = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
        {
            if (d.Delegate == null) continue;
            tools.Add(d.Descriptor);
            calls[d.Descriptor.Name] = d.Delegate;
        }

        _tools = tools;
        _calls = calls;
        _invoker = invoker ?? DirectDelegateInvoker.Instance;
    }

    public string Backend => ToolBackend.CSharp;

    public IReadOnlyCollection<ToolDescriptor> Tools => _tools;

    public bool CanCall(string name) => _calls.ContainsKey(name);

    public async Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(name, out var function))
            return ToolExecutionResult.Fail($"C#Track 无名为 '{name}' 的可调用工具");

        ct.ThrowIfCancellationRequested();

        object?[] args;
        try
        {
            args = BuildArguments(function, argumentsJson);
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Fail($"执行 '{name}' 失败: {ex.InnerException?.Message ?? ex.Message}");
        }

        // 真实调用与结果映射交执行后端（缺省 DirectDelegateInvoker，宿主可注入统一执行栈）。
        return await _invoker.InvokeAsync(function, args, ct).ConfigureAwait(false);
    }

    /// <summary>解析 JSON 入参（对象映射 name→value），按委托形参位置组装实参；缺失用可选默认值。</summary>
    private static object?[] BuildArguments(Delegate function, string? argumentsJson)
    {
        var parameters = function.Method.GetParameters();
        if (parameters.Length == 0)
            return Array.Empty<object?>();

        var positional = new object?[parameters.Length];
        var named = ParseArguments(argumentsJson);

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];

            if (named.TryGetValue(p.Name ?? $"{i}", out var value))
            {
                positional[i] = Convert(value, p.ParameterType);
            }
            else if (p.HasDefaultValue)
            {
                positional[i] = p.DefaultValue;
            }
            else
            {
                throw new InvalidOperationException($"缺少参数 '{p.Name}'（类型 {p.ParameterType.Name}）");
            }
        }

        return positional;
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseArguments(string? argumentsJson)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argumentsJson)) return map;

        using var doc = JsonDocument.Parse(argumentsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("入参必须是 JSON 对象 { \"参数名\": 值 }");

        foreach (var prop in doc.RootElement.EnumerateObject())
            map[prop.Name] = prop.Value.Clone();

        return map;
    }

    /// <summary>把 <see cref="JsonElement"/> 值转换为目标参数类型（覆盖平台常用标量；其它交给 JsonSerializer 反序列化）。</summary>
    private static object? Convert(JsonElement value, Type target)
    {
        if (target == typeof(object) || target == typeof(JsonElement))
            return value.ValueKind == JsonValueKind.Null ? null : value.Clone();

        if (target == typeof(string))
            return value.GetString();
        if (target == typeof(bool))
            return value.GetBoolean();
        if (target == typeof(int))
            return value.GetInt32();
        if (target == typeof(long))
            return value.GetInt64();
        if (target == typeof(double))
            return value.GetDouble();
        if (target == typeof(decimal))
            return value.GetDecimal();
        if (target == typeof(DateTime))
            return value.GetDateTime();
        if (target == typeof(Guid))
            return value.GetGuid();
        if (target.IsEnum && value.ValueKind == JsonValueKind.String)
            return Enum.Parse(target, value.GetString()!, ignoreCase: true);

        // 复杂类型：按目标类型反序列化。
        return JsonSerializer.Deserialize(value.GetRawText(), target);
    }
}
