namespace Strategy.Observability.Ports;

/// <summary>
/// 延迟动作载体契约（机制件）：绑定不即时求值，把动作交给调用方在断言期执行
/// （测试内核的 <c>throws</c> 语义即此形态）。L1 只出接口与默认实现，具体载体由调用方转译。
/// </summary>
public interface IDeferredAction
{
    /// <summary>即时求值（异常原样上抛）。</summary>
    object? Invoke();
}

/// <summary>
/// 问题清单载体契约（机制件）：空集合 = 全部通过；供 <c>issueCount</c> / <c>issueContains</c> 直接断言。
/// </summary>
public interface IPortIssues
{
    /// <summary>校验问题清单（空集合 = 全部通过）。</summary>
    IReadOnlyList<string> Issues { get; }
}

/// <summary>延迟动作默认实现（声明 <c>deliver.defer</c> 时由 <see cref="ScriptedBinding"/> 产出）。</summary>
public sealed class DeferredAction : IDeferredAction
{
    private readonly Func<object?> _invoke;

    public DeferredAction(Func<object?> invoke)
        => _invoke = PortGuard.Required(invoke, nameof(invoke));

    public object? Invoke() => _invoke();
}

/// <summary>问题清单默认实现（声明 <c>deliver.issues</c> 时由 <see cref="ScriptedBinding"/> 产出）。</summary>
public sealed class PortIssues : IPortIssues
{
    public PortIssues(IReadOnlyList<string> issues)
        => Issues = PortGuard.Required(issues, nameof(issues));

    public IReadOnlyList<string> Issues { get; }
}
