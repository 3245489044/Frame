namespace Share.ContextGeometry;

/// <summary>上下文事件种类（委托事件日志的最小词表）。</summary>
public enum ContextEventKind
{
    /// <summary>系统提示（前缀段来源）。</summary>
    System,

    /// <summary>用户输入。</summary>
    User,

    /// <summary>助手输出。</summary>
    Assistant,

    /// <summary>工具结果。</summary>
    Tool,

    /// <summary>委托事件（工具调用意图 → 子智能体）。</summary>
    Delegated,

    /// <summary>无法识别的历史种类（读侧保留原值，禁静默归入 Tool）。</summary>
    Unknown,
}

/// <summary>投影出的工具调用（内核中立形态；不引用领域 LLM 契约）。</summary>
public readonly record struct ProjectedToolCall(string Name, string Arguments, string? Id = null);

/// <summary>上下文事件（只追加；键稳定，禁重写已发射项）。</summary>
public readonly record struct ContextEvent(
    string Key,
    ContextEventKind Kind,
    string Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<ProjectedToolCall>? ToolCalls = null);

/// <summary>投影出的单条消息（仅追加视图）。</summary>
public sealed record ProjectedMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<ProjectedToolCall>? ToolCalls = null);

/// <summary>投影结果（消息序列 + 诊断）。</summary>
public sealed record ProjectedMessages(IReadOnlyList<ProjectedMessage> Messages, IReadOnlyList<string> Diagnostics)
{
    /// <summary>空投影。</summary>
    public static readonly ProjectedMessages Empty = new(Array.Empty<ProjectedMessage>(), Array.Empty<string>());
}

/// <summary>
/// 上下文投影（纯函数）：事件日志 → 消息序列。
/// <para>硬约束：仅追加不回溯 —— <c>Project(log+[e]).Take(n) == Project(log)</c>；同一日志两次投影逐字相等。</para>
/// </summary>
public static class ContextProjection
{
    /// <summary>追加一条事件（返回新列表；不改入参）。</summary>
    public static IReadOnlyList<ContextEvent> Append(IReadOnlyList<ContextEvent>? log, ContextEvent evt)
    {
        var next = new List<ContextEvent>(log?.Count ?? 0 + 1);
        if (log is { Count: > 0 }) next.AddRange(log);
        next.Add(evt);
        return next;
    }

    /// <summary>投影（角色/内容缺失 ⇒ 显式诊断并保留占位，不静默丢弃语义）。</summary>
    public static ProjectedMessages Project(IReadOnlyList<ContextEvent>? log)
    {
        if (log is not { Count: > 0 }) return ProjectedMessages.Empty;

        var messages = new List<ProjectedMessage>(log.Count);
        var diagnostics = new List<string>();
        foreach (var evt in log)
        {
            if (string.IsNullOrWhiteSpace(evt.Role))
            {
                diagnostics.Add($"{GeometryDiagnostics.ProjectionInvalid}:role:{evt.Key}");
                continue;
            }

            if (string.IsNullOrEmpty(evt.Content))
            {
                diagnostics.Add($"{GeometryDiagnostics.ProjectionInvalid}:content:{evt.Key}");
            }

            messages.Add(new ProjectedMessage(evt.Role, evt.Content ?? string.Empty, evt.ToolCallId, evt.ToolCalls));
        }
        return new ProjectedMessages(messages, diagnostics);
    }

    /// <summary>按种类映射为角色（表驱动；未知种类 ⇒ 原角色，不猜语义）。</summary>
    public static string RoleFor(ContextEventKind kind)
        => kind switch
        {
            ContextEventKind.System => "system",
            ContextEventKind.User => "user",
            ContextEventKind.Assistant => "assistant",
            ContextEventKind.Tool => "tool",
            ContextEventKind.Delegated => "assistant",
            _ => "user",
        };
}
