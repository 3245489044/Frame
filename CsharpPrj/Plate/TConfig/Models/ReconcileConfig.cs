
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
/// <para>
/// 多路复用：同一注册函数承载多个 <c>data-cmd</c> 分支时，用 <see cref="Cases"/> 为各 cmd 分别声明契约
/// （每条 case = 一份按 <c>when</c> 选择的子契约）；单契约场景直接用 <see cref="Signals"/> +
/// <see cref="When"/>。
/// </para>
/// </summary>
public class ReconcileConfig
{
    /// <summary>对账模式，取值见 <see cref="ReconcileModes"/>；缺省 off。</summary>
    public string? Mode { get; set; }

    /// <summary>顺序语义，取值见 <see cref="ReconcileOrders"/>；缺省 strict。</summary>
    public string? Order { get; set; }

    /// <summary>
    /// 命令选择器（<c>bindings.reconcile.when</c>）：契约粒度修正 —— 同一注册函数承载多个
    /// <c>data-cmd</c> 分支（消息桥多路复用）时，按事件载荷 <c>payload.cmd</c> 选择本契约；
    /// 命中 cmd 的契约优先于默认契约（<see cref="When"/> 为空者）。缺省 null/空 = 默认契约。
    /// </summary>
    public string? When { get; set; }

    /// <summary>应发信号期望集（须在语义词表内）。单契约场景使用。</summary>
    public List<SignalExpectation>? Signals { get; set; }

    /// <summary>按 cmd 分列的契约集（多路复用场景；与 <see cref="Signals"/> 二选一，非空时优先）。</summary>
    public List<ReconcileCase>? Cases { get; set; }
}

/// <summary>按命令选择的子契约（<c>bindings.reconcile.cases[]</c>）：<c>when</c> + 该 cmd 应发信号集。</summary>
public class ReconcileCase
{
    /// <summary>命令选择器（payload.cmd）；null/空 = 该函数的默认契约。</summary>
    public string? When { get; set; }

    /// <summary>顺序语义覆盖（缺省继承外层 <see cref="ReconcileConfig.Order"/>）。</summary>
    public string? Order { get; set; }

    /// <summary>该 cmd 的应发信号期望集（须在语义词表内）。</summary>
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

    /// <summary>
    /// 归属键名（G-3 键名映射）：声明后只比对载荷中**该字段**的值与 <see cref="Scope"/>
    /// （如 UI 侧 <c>taskKey</c> ↔ 载荷字段 <c>taskKey</c>），避免全串子串误配；
    /// 未声明时只认 <c>ScopeId</c> 精确匹配（不再做载荷子串兜底，防止 "user:1" 命中 "user:10"）。
    /// </summary>
    public string? ScopeKey { get; set; }

    /// <summary>阶段约束（可选）。</summary>
    public string? Phase { get; set; }

    /// <summary>载荷子串断言（值层，可选降级；不作对账主干）。</summary>
    public string? Payload { get; set; }

    /// <summary>是否为终止信号（如 TaskCompleted/ToolSucceeded）：缺失即判未终止。</summary>
    public bool Terminal { get; set; }
}
