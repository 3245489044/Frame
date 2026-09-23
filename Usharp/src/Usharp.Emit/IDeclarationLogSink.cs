using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 声明面装载告警端口：本层**不持有日志键**（键名归领域），只上报结构化事件 + 事实；
/// 消费方的适配器据事件映射到自己的日志键与载荷（键名逐字保持）。
/// </summary>
public enum DeclarationLogEvent
{
    /// <summary>声明面未解析到路径 / 文件不存在（未激活，不假装通过）。</summary>
    NotDeclared,

    /// <summary>编译失败（闸A 错误）：载荷 = 错误条数。</summary>
    CompileFailed,

    /// <summary>单条编译诊断：载荷 = <c>UsharpDiagnostic</c>。</summary>
    CompileDiagnostic,

    /// <summary>发射失败（产物为空 / 发射期错误）：载荷 = 诊断条数。</summary>
    EmitFailed,

    /// <summary>单条发射诊断：载荷 = <c>UsharpDiagnostic</c>。</summary>
    EmitDiagnostic,

    /// <summary>装载成功：载荷 = 发射产物（消费方自行取数，如步骤数 / 根层 id）。</summary>
    Loaded,

    /// <summary>装载期异常：载荷 = <see cref="Exception"/>（不吞，转可见化）。</summary>
    Exception,
}

/// <summary>声明面装载告警端口（结构化事件 + 事实；键名与载荷由消费方决定）。</summary>
public interface IDeclarationLogSink
{
    /// <summary>上报一次装载事件。</summary>
    /// <param name="event">事件类型。</param>
    /// <param name="baseName">逻辑名。</param>
    /// <param name="flowId">目标流号（无流概念时为 null）。</param>
    /// <param name="path">已解析路径（未解析时为 null）。</param>
    /// <param name="payload">事实载荷（计数 / 诊断 / 产物 / 异常）。</param>
    void Report(DeclarationLogEvent @event, string baseName, string? flowId, string? path, object? payload);
}

/// <summary>委托式告警端口（消费方以 lambda 落既有日志键）。</summary>
public sealed class DelegateDeclarationLogSink : IDeclarationLogSink
{
    private readonly Action<DeclarationLogEvent, string, string?, string?, object?> _report;

    /// <summary>构筑端口。</summary>
    /// <param name="report">上报委托（事件, 逻辑名, 流号, 路径, 载荷）。</param>
    public DelegateDeclarationLogSink(Action<DeclarationLogEvent, string, string?, string?, object?> report)
        => _report = report ?? throw new ArgumentNullException(nameof(report));

    /// <inheritdoc />
    public void Report(DeclarationLogEvent @event, string baseName, string? flowId, string? path, object? payload)
        => _report(@event, baseName, flowId, path, payload);
}
