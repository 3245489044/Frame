using Modules.Node.Spec;

namespace Modules.Node.Diagnostics;

/// <summary>框架日志端口（事件名 + 载荷）；宿主以实例注入，禁进程静态。</summary>
public interface INodeFrameworkLog
{
    void Log(string eventName, object payload);
}

/// <summary>
/// 定义期诊断端口（<see cref="IDefinitionDiagnostics"/>）的**通用落地**：语言/宿主侧定义期诊断
/// 单向流入框架诊断（合并）与宿主日志（可观测）；禁反向解析。
/// <para>
/// 事件名 / 日志端口以构造参数注入（零领域词汇）；未给端口 ⇒ 仅落诊断 sink（静默可观测面）。
/// </para>
/// </summary>
public sealed class LoggingDefinitionDiagnostics : IDefinitionDiagnostics
{
    /// <summary>无端口的独立实例（仅落诊断 sink）。</summary>
    public static readonly LoggingDefinitionDiagnostics Silent = new();

    private readonly INodeFrameworkLog? _log;
    private readonly INodeDiagnosticsSink? _sink;
    private readonly string _eventName;

    /// <param name="log">宿主日志端口（可选：多域共用同一日志时按 Subject 归因）。</param>
    /// <param name="sink">框架诊断接收端（可选）。</param>
    /// <param name="eventName">诊断日志事件名（单一静态键；域区分靠载荷）。</param>
    public LoggingDefinitionDiagnostics(
        INodeFrameworkLog? log = null,
        INodeDiagnosticsSink? sink = null,
        string eventName = "node-framework:definition-diagnostic")
    {
        _log = log;
        _sink = sink;
        _eventName = eventName;
    }

    /// <inheritdoc />
    public void Report(string code, NodeDiagnosticSeverity severity, string message, string? subject = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _sink?.Report(new NodeDiagnostic(code, severity, message, subject));
        _log?.Log(_eventName, new { code, severity = severity.ToString(), message, subject });
    }
}
