using System;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 服务装配兜底组合子 —— 「取服务 → 未装配返回兜底诊断 → 否则执行」的统一骨架，
/// 消除各 UI/脚本库手写 `if (svc is null) return XXNotAttached;` 同构重复（SingleImplementation / TH-12）。
/// 仅依赖 <see cref="ErrorClassifier"/>（同域），不依赖任何使用层类型；消费方以委托注入服务获取与动作。
/// 下沉自 Agent.Launcher AgentUiLibrarySupport 的 With* 骨架（Agent 侧保留签名、内部委托本组合子）。
/// </summary>
public static class Guard
{
    /// <summary>统一「服务未装配 → notAttached 兜底」异步骨架：get 取服务，null → notAttached，否则执行 action。</summary>
    public static Task<TResult> FallbackAsync<TService, TResult>(
        Func<TService?> get, TResult notAttached, Func<TService, Task<TResult>> action)
        where TService : class
    {
        PortGuard.Required(get, nameof(get));
        PortGuard.Required(action, nameof(action));
        var svc = get();
        if (svc is null) return Task.FromResult(notAttached);
        return action(svc);
    }

    /// <summary>统一「服务未装配 → notAttached 兜底」同步骨架（对称于 <see cref="FallbackAsync"/>，供同步返回函数复用）。</summary>
    public static TResult Fallback<TService, TResult>(
        Func<TService?> get, TResult notAttached, Func<TService, TResult> action)
        where TService : class
    {
        PortGuard.Required(get, nameof(get));
        PortGuard.Required(action, nameof(action));
        var svc = get();
        if (svc is null) return notAttached;
        return action(svc);
    }

    /// <summary>读取失败诊断（键 + 消息 + 调用方上下文；由消费方决定日志出口）。</summary>
    public readonly record struct ReadDiagnostic(string Key, string Message, object? Context);

    /// <summary>
    /// 统一「可选读取 → 失败记诊断 → 显式空态原因」骨架：成功返回 (值, null)；失败返回 (null, 空态文案)。
    /// <para>收拢各视图服务手写的 <c>try { read } catch { log; empty = "（…失败）" }</c> 同构块；
    /// 框架只产诊断事实与空态原因，日志出口由消费方注入（保持零日志依赖）。</para>
    /// </summary>
    public static async Task<(T? Value, string? EmptyReason)> TryReadAsync<T>(
        string diagnosticKey,
        object? context,
        string emptyReason,
        Func<Task<T>> read,
        Action<ReadDiagnostic>? onFailure = null)
        where T : class
    {
        PortGuard.Required(read, nameof(read));
        try
        {
            return (await read().ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(new ReadDiagnostic(diagnosticKey, ex.Message, context));
            return (null, emptyReason);
        }
    }

    /// <summary>统一「服务未装配诊断 + 异步动作异常分类」骨架：get 取服务，null → notAttached；
    /// 否则 try 包 action，catch 经 <see cref="ErrorClassifier.ClassifyError"/> 归类后输出「&lt;label&gt;失败[cls]: msg」。</summary>
    public static async Task<string> ClassifiedAsync<TService>(
        Func<TService?> get, string notAttached, string label, Func<TService, Task<string>> action)
        where TService : class
    {
        PortGuard.Required(get, nameof(get));
        PortGuard.Required(action, nameof(action));
        var svc = get();
        if (svc is null) return notAttached;
        try
        {
            return await action(svc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var cls = ErrorClassifier.ClassifyError(ex);
            return $"{label}失败[{cls}]: {ex.Message}";
        }
    }
}
