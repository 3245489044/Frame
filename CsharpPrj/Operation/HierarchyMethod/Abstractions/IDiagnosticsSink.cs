using System;

namespace Operation.HierarchyMethod.Abstractions;

/// <summary>
/// 诊断可见化端口（DUP-7 / ERR-5·7·8 收敛）—— 框架侧**唯一**失败 / 降级可见化载体。
/// <para>
/// 分层无环：框架只依赖本接口，不感知 Agent 诊断类型；组合根在装配期绑定具体实现
/// （Agent 侧经 <see cref="DelegateDiagnosticsSink"/> 绑定 <c>AgentLog</c> 门面）。
/// </para>
/// <para>
/// 语义：<see cref="Report"/> 一律「只记不改」—— 不抛、不阻断、不兜底；调用方按各自口径决定后续
/// （继续 / 返回 ∅ / 抛），从而消除「同一失败在 A 处记日志、B 处静默、C 处返回空集」的口径分叉。
/// </para>
/// </summary>
public interface IDiagnosticsSink
{
    /// <summary>记录一次诊断（<paramref name="code"/> 取 <see cref="DiagnosticsCodes"/> 常量；载荷为匿名对象）。</summary>
    /// <param name="code">诊断码（稳定标识，供门禁 / 检索对账）。</param>
    /// <param name="payload">结构化载荷（缺省 null = 无附加）。</param>
    void Report(string code, object? payload = null);
}

/// <summary>
/// 空诊断端口（缺省装配）：不记任何诊断 —— 对齐「未装配返回诊断常量、不阻断 DB-only / 确定性路径」。
/// </summary>
public sealed class NullDiagnosticsSink : IDiagnosticsSink
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NullDiagnosticsSink Instance = new();

    private NullDiagnosticsSink()
    {
    }

    /// <inheritdoc />
    public void Report(string code, object? payload = null)
    {
    }
}

/// <summary>
/// 委托适配器：把既有 <c>Action&lt;string, object?&gt;</c> 形态日志门面（如 <c>AgentLog.Log</c>）
/// 适配为本端口，使组合根无需新增类型即可完成绑定。
/// </summary>
public sealed class DelegateDiagnosticsSink : IDiagnosticsSink
{
    private readonly Action<string, object?> _log;

    /// <param name="log">委托日志门面（诊断码 + 载荷）。</param>
    public DelegateDiagnosticsSink(Action<string, object?> log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public void Report(string code, object? payload = null) => _log(code, payload);
}
