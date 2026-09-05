#nullable enable

using System.Threading;
using System.Threading.Tasks;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 注册表函数调用器——ITrConfigRegistry → HtmlEventBridge.HtmlFunctionInvoker 的统一实现：
/// 按形参名装配参数（CancellationToken 剔除/回填）→ 基础类型转换 → ITrConfig.Invoke → Task 解包为结果字符串。
/// [搬运自 AgentUiLibraryHtml.CreateInvoker 全链路；目标无关，仅依赖 TConfig.ITrConfigRegistry]
/// </summary>
public sealed class TrConfigFunctionInvoker
{
    private readonly ITrConfigRegistry _registry;

    public TrConfigFunctionInvoker(ITrConfigRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>按函数名调用注册表函数，返回结果字符串（未注册/失败返回诊断文本，不抛）。</summary>
    public async Task<string> InvokeAsync(string functionName, IReadOnlyDictionary<string, object?> parameters)
    {
        if (!_registry.TryGetConfig(functionName, out var config))
            return $"函数未注册: {functionName}";
        try
        {
            var args = BuildArguments(config, parameters);
            var result = config.Invoke(args);
            return await UnwrapAsync(config, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"函数执行失败[{ex.GetType().Name}]: {ex.Message}";
        }
    }

    /// <summary>适配事件桥/脚本桥端口（HtmlEventBridge.HtmlFunctionInvoker 委托）。</summary>
    public static HtmlEventBridge.HtmlFunctionInvoker ToBridgeInvoker(ITrConfigRegistry registry)
        => new TrConfigFunctionInvoker(registry).InvokeAsync;

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

    private static async Task<string> UnwrapAsync(ITrConfig config, object? result)
    {
        if (!config.IsAsync || result is not Task task)
            return result?.ToString() ?? string.Empty;
        try
        {
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp?.GetValue(task)?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return $"函数执行失败[{inner.GetType().Name}]: {inner.Message}";
        }
    }
}
