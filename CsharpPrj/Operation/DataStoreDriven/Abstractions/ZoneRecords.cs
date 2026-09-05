namespace Operation.DataStoreDriven.Abstractions;

/// <summary>输入区记录（ur_inputs）。写入即触发信号源：提交 = 发布 = 触发。</summary>
public sealed class InputRecord
{
    public long Id { get; set; }

    public string? ThreadId { get; set; }

    public string? InputKey { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public string? CreatedAt { get; set; }
}

/// <summary>输出区记录（ur_outputs）。AI/处理器结果；落库由框架在循环边界决定。</summary>
public sealed class OutputRecord
{
    public long Id { get; set; }

    public string? ThreadId { get; set; }

    public string? InputKey { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public string? Status { get; set; }

    public string? CreatedAt { get; set; }
}

/// <summary>事件区记录（ur_events，观测即查数据）。</summary>
public sealed class EventRecord
{
    public long Id { get; set; }

    public string? ThreadId { get; set; }

    public string? EventType { get; set; }

    public string? EventKey { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public string? CreatedAt { get; set; }
}
