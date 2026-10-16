
namespace TConfig;

/// <summary>
/// 对账模式（<c>bindings.reconcile.mode</c>）：
/// <see cref="Off"/> 不对账；<see cref="Warn"/> 失配仅落事件不抛；<see cref="Strict"/> 失配结构化抛错。
/// </summary>
public static class ReconcileModes
{
    public const string Off = "off";
    public const string Warn = "warn";
    public const string Strict = "strict";
}

/// <summary>对账顺序语义（<c>bindings.reconcile.order</c>）：<see cref="Strict"/> 须按声明序成链，<see cref="Any"/> 只查存在。</summary>
public static class ReconcileOrders
{
    public const string Any = "any";
    public const string Strict = "strict";
}

/// <summary>
/// 触发后置对账契约（<c>bindings.reconcile</c>）：该 UI 触发「应发」的 EventKind 签名集（信号回环核对）。
/// 由 <see cref="EventBinding.Reconcile"/> 承载；缺省 null = 不对账（零回归）。
/// </summary>
public class ReconcileConfig
{
    /// <summary>对账模式，取值见 <see cref="ReconcileModes"/>；缺省 off。</summary>
    public string? Mode { get; set; }

    /// <summary>顺序语义，取值见 <see cref="ReconcileOrders"/>；缺省 strict。</summary>
    public string? Order { get; set; }

    /// <summary>应发信号期望集（须在语义词表内）。</summary>
    public List<SignalExpectation>? Signals { get; set; }
}

/// <summary>
/// 单条信号期望：声明某次触发「应发」的 EventKind（含可选归属/阶段/载荷/终止约束）。
/// </summary>
public class SignalExpectation
{
    /// <summary>必发 EventKind（须在语义词表内）。</summary>
    public string? Signal { get; set; }

    /// <summary>归属约束（ScopeId/TaskKey）；null = 不约束归属。</summary>
    public string? Scope { get; set; }

    /// <summary>阶段约束（可选）。</summary>
    public string? Phase { get; set; }

    /// <summary>载荷子串断言（值层，可选降级；不作对账主干）。</summary>
    public string? Payload { get; set; }

    /// <summary>是否为终止信号（如 TaskCompleted/ToolSucceeded）：缺失即判未终止。</summary>
    public bool Terminal { get; set; }
}
