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

// ─────────────────────────────────────────────────────────────────────────────
// E14 诊断族：边界/端口装配期与调用期的统一诊断（替代测试侧手写 SpecRunException）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>解析端口键时未找到登记（含当前已登记能力清单，防拼写漂移静默通过）。</summary>
public sealed class PortNotRegisteredException : ObservabilityException
{
    public string PortKey { get; }

    public PortNotRegisteredException(string portKey, IEnumerable<string> available)
        : base($"端口 '{portKey}' 未登记。已登记：{(available.Any() ? string.Join(", ", available) : "(空)")}")
    {
        PortKey = portKey;
    }
}

/// <summary>解析边界契约键时未找到登记（含能力清单）。</summary>
public sealed class BoundaryNotRegisteredException : ObservabilityException
{
    public string BoundaryKey { get; }

    public BoundaryNotRegisteredException(string boundaryKey, IEnumerable<string> available)
        : base($"边界契约 '{boundaryKey}' 未登记。已登记：{(available.Any() ? string.Join(", ", available) : "(空)")}")
    {
        BoundaryKey = boundaryKey;
    }
}

/// <summary>同一键重复登记（单一实现点被破坏）。</summary>
public sealed class DuplicateBoundaryException : ObservabilityException
{
    public string BoundaryKey { get; }

    public DuplicateBoundaryException(string boundaryKey)
        : base($"边界键 '{boundaryKey}' 重复登记（同一实现点只允许一个载体）。")
    {
        BoundaryKey = boundaryKey;
    }
}

/// <summary>边界契约违规（字段缺失 / 类型不符 / 枚举越界 / 必填依赖缺失）。</summary>
public sealed class BoundaryContractViolationException : ObservabilityException
{
    public string BoundaryKey { get; }

    public BoundaryContractViolationException(string boundaryKey, string issue)
        : base($"{boundaryKey} 契约违规: {issue}")
    {
        BoundaryKey = boundaryKey;
    }
}

/// <summary>端口已登记但未绑定替身/实现（装配缺口）。</summary>
public sealed class FakeBindingMissingException : ObservabilityException
{
    public string PortKey { get; }

    public FakeBindingMissingException(string portKey)
        : base($"端口 '{portKey}' 未绑定实现或替身（装配缺口）。")
    {
        PortKey = portKey;
    }
}

/// <summary>负向守卫替身被调用（用例声明该端口不应被触达）。</summary>
public sealed class PortCallNotExpectedException : ObservabilityException
{
    public string PortKey { get; }

    public PortCallNotExpectedException(string portKey)
        : base($"端口 '{portKey}' 被调用，但用例声明该调用不应发生。")
    {
        PortKey = portKey;
    }
}

/// <summary>按序应答替身已耗尽（声明序列与实际调用次数不符）。</summary>
public sealed class FakeSequenceExhaustedException : ObservabilityException
{
    public string PortKey { get; }

    public FakeSequenceExhaustedException(string portKey, int provided)
        : base($"端口 '{portKey}' 的按序应答已耗尽（已提供 {provided} 个应答）。")
    {
        PortKey = portKey;
    }
}

