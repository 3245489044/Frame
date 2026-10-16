namespace Operation.ReconcileDriven.Reconcile;

/// <summary>
/// 对账判据号（稳定编号，供报错/诊断引用）：
/// C 系列 = 装载期判据（配置即拒载）；R 系列 = 运行期四类失配。
/// </summary>
public static class ReconcileCodes
{
    /// <summary>C1：<c>signals[].signal</c> 须在语义词表内。</summary>
    public const string C1UnknownSignal = "C1";

    /// <summary>C2：<c>mode≠off</c> 时 <c>signals</c> 非空（空契约恒真，拒绝静默通过）。</summary>
    public const string C2EmptyContract = "C2";

    /// <summary>C3：绑定事件函数名须在函数注册表内（替换原静默 Skipped）。</summary>
    public const string C3UnregisteredFunction = "C3";

    /// <summary>C4：<c>parameters</c> 中 Required 源控件/键可解析。</summary>
    public const string C4ParameterUnresolved = "C4";

    /// <summary>C5：<c>bindings.output</c> 目标控件在渲染树/状态面存在。</summary>
    public const string C5OutputMissing = "C5";

    /// <summary>C6：<c>order=strict</c> 时序列可拓扑成链（无自相矛盾）。</summary>
    public const string C6OrderNotChainable = "C6";

    /// <summary>R1：应发未发（完整性失配；缺失项即断点）。</summary>
    public const string R1Missing = "R1";

    /// <summary>R2：顺序倒置（order=strict 下未按声明序成链）。</summary>
    public const string R2OutOfOrder = "R2";

    /// <summary>R3：终止信号缺失（未回环到终止）。</summary>
    public const string R3TerminalMissing = "R3";

    /// <summary>R4：归属串台（ScopeId/TraceId 不属本次触发）。</summary>
    public const string R4ScopeMismatch = "R4";
}
