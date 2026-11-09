#nullable enable

namespace SurfacePaint.Contracts;

/// <summary>端口类别（诊断/门禁用；不作为行为分派依据）。</summary>
public enum PortKind
{
    /// <summary>运行时光。</summary>
    Light,

    /// <summary>窗口帧。</summary>
    Frame,

    /// <summary>引擎帧。</summary>
    Engine,

    /// <summary>背后快照。</summary>
    Backdrop,

    /// <summary>取数声明。</summary>
    Gather,

    /// <summary>事件日志。</summary>
    Log,
}

/// <summary>端口就绪态（结构事实）：缺失/未就绪/就绪必须可区分，禁止用“调用后返回垃圾”表达。</summary>
public enum PortReadiness
{
    /// <summary>实现未装配（注册点必须拒绝注册）。</summary>
    Missing,

    /// <summary>已装配但依赖未就绪（调用点必须拒调）。</summary>
    NotReady,

    /// <summary>可调用。</summary>
    Ready,
}

/// <summary>
/// 端口状态快照（不可变）。
/// <para>纪律：<see cref="PortReadiness.Ready"/> 之外不得调用端口的业务方法；缺失即不注册。</para>
/// </summary>
/// <param name="Kind">端口类别。</param>
/// <param name="Readiness">就绪态。</param>
/// <param name="Reason">原因码（<see cref="SurfaceReason"/> 的字符串形式；Ready 时为空）。</param>
public readonly record struct PortStatus(PortKind Kind, PortReadiness Readiness, string Reason)
{
    /// <summary>就绪。</summary>
    public static PortStatus Ready(PortKind kind) => new(kind, PortReadiness.Ready, string.Empty);

    /// <summary>缺失（附带原因码）。</summary>
    public static PortStatus Missing(PortKind kind, string reason)
        => new(kind, PortReadiness.Missing, reason);

    /// <summary>未就绪（附带原因码）。</summary>
    public static PortStatus NotReady(PortKind kind, string reason)
        => new(kind, PortReadiness.NotReady, reason);

    /// <summary>是否可调用。</summary>
    public bool IsReady => Readiness == PortReadiness.Ready;
}
