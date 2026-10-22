using System;

namespace Share.Foundations.Diagnostics;

/// <summary>
/// 诊断可见化端口（框架侧唯一失败/降级可见化载体）。
/// <para>框架只依赖本接口，不感知应用层诊断类型；组合根在装配期绑定具体实现。</para>
/// </summary>
public interface IDiagnosticsSink
{
    /// <summary>记录一次诊断（只记不改：不抛、不阻断、不兜底）。</summary>
    void Report(string code, object? payload = null);
}

/// <summary>空诊断端口（缺省装配）：不记任何诊断。</summary>
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

/// <summary>委托适配器：把既有 (code, payload) 日志门面适配为本端口。</summary>
public sealed class DelegateDiagnosticsSink : IDiagnosticsSink
{
    private readonly Action<string, object?> _log;

    /// <param name="log">委托日志门面（诊断码 + 载荷）。</param>
    public DelegateDiagnosticsSink(Action<string, object?> log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public void Report(string code, object? payload = null) => _log(code, payload);
}
