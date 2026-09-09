namespace Strategy.Observability.Diagnostics;

/// <summary>观测内核统一异常基类。错误一律在调用边界抛出，不再经日志插槽静默降级。</summary>
public class ObservabilityException : Exception
{
    public ObservabilityException(string message) : base(message) { }
    public ObservabilityException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>Emit 指向未注册的事件分类（契约即断言：新增观测点请先 Register 契约）。</summary>
public sealed class UnregisteredEventException : ObservabilityException
{
    public string EventKind { get; }

    public UnregisteredEventException(string eventKind)
        : base($"未注册事件分类 '{eventKind}'：新增观测点请先注册契约。")
    {
        EventKind = eventKind;
    }
}

/// <summary>事件经契约校验失败（字段缺失 / 度量缺失 / 实体类型不符 / 枚举违规）。</summary>
public sealed class ContractValidationException : ObservabilityException
{
    public string Issue { get; }

    public ContractValidationException(string issue)
        : base($"事件校验失败: {issue}")
    {
        Issue = issue;
    }
}

/// <summary>调用方显式 FlushAsync 落库失败（Sink 不再内嵌重试/静默记录，缓冲保留可重放）。</summary>
public sealed class SinkWriteException : ObservabilityException
{
    public SinkWriteException(string message, Exception? inner = null) : base(message, inner) { }
}
