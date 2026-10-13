#nullable enable

using System.Threading;
using System.Threading.Tasks;
using TConfig;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 注册表函数调用器——ITrConfigRegistry → HtmlEventBridge.HtmlFunctionInvoker 的统一实现：
/// 按形参名装配参数（CancellationToken 剔除/回填）→ 基础类型转换 → ITrConfig.Invoke → Task 解包为
/// 类型化 <see cref="RenderOutput"/>（返回 <see cref="RenderOutput"/> 的函数产物原样透传；其余按文本叙述）。
/// [搬运自 AgentUiLibraryHtml.CreateInvoker 全链路；目标无关，仅依赖 TConfig.ITrConfigRegistry]
/// </summary>
public sealed class TrConfigFunctionInvoker
{
    private readonly ITrConfigRegistry _registry;

    public TrConfigFunctionInvoker(ITrConfigRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>按函数名调用注册表函数，返回类型化产物（未注册/失败返回诊断文本，不抛）。
    /// 函数返回 <see cref="RenderOutput"/> 时原样透传（P0：领域只产产物）；否则包为叙述文本。</summary>
    public async Task<RenderOutput> InvokeAsync(string functionName, IReadOnlyDictionary<string, object?> parameters)
    {
        if (!_registry.TryGetConfig(functionName, out var config))
            return RenderOutput.Narrative($"函数未注册: {functionName}");
        try
        {
            var args = BuildArguments(config, parameters);
            var result = config.Invoke(args);
            var raw = await UnwrapAsync(config, result).ConfigureAwait(false);
            return raw switch
            {
                RenderOutput output => output,
                null => RenderOutput.Skip,
                string s => RenderOutput.Narrative(s),
                _ => RenderOutput.Narrative(raw.ToString()),
            };
        }
        catch (Exception ex)
        {
            return RenderOutput.Narrative($"函数执行失败[{ex.GetType().Name}]: {ex.Message}");
        }
    }

    /// <summary>适配事件桥端口（HtmlEventBridge.HtmlFunctionInvoker 委托，类型化产物）。</summary>
    public static HtmlEventBridge.HtmlFunctionInvoker ToBridgeInvoker(ITrConfigRegistry registry)
        => new TrConfigFunctionInvoker(registry).InvokeAsync;

    /// <summary>适配脚本桥端口（文本结果；计划产物取其文本叙述）。</summary>
    public static HtmlScriptBridge.HtmlScriptInvoker ToScriptInvoker(ITrConfigRegistry registry)
    {
        var invoker = new TrConfigFunctionInvoker(registry);
        return async (name, parameters) => (await invoker.InvokeAsync(name, parameters).ConfigureAwait(false)).Text ?? string.Empty;
    }

    private static object?[] BuildArguments(ITrConfig config, IReadOnlyDictionary<string, object?> parameters)
    {
        var args = new object?[config.Parameters.Count];
        for (int i = 0; i < config.Parameters.Count; i++)
        {
            var p = config.Parameters[i];
            if (p.Type == typeof(CancellationToken))
            {
                args[i] = CancellationToken.None;
                continue;
            }
            if (parameters.TryGetValue(p.Name, out var v) && v is not null)
                args[i] = ConvertValue(v, p.Type);
            else if (p.IsOptional)
                args[i] = p.DefaultValue;
        }
        return args;
    }

    private static object? ConvertValue(object value, Type targetType)
    {
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType == typeof(string)) return value.ToString();
        var text = value.ToString() ?? string.Empty;
        if (targetType == typeof(int) && int.TryParse(text, out var i)) return i;
        if (targetType == typeof(double) && double.TryParse(text, out var d)) return d;
        if (targetType == typeof(bool) && bool.TryParse(text, out var b)) return b;
        if (targetType == typeof(long) && long.TryParse(text, out var l)) return l;
        return value;
    }

    private static async Task<object?> UnwrapAsync(ITrConfig config, object? result)
    {
        if (!config.IsAsync || result is not Task task)
            return result;
        try
        {
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp?.GetValue(task);
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return $"函数执行失败[{inner.GetType().Name}]: {inner.Message}";
        }
    }
}
