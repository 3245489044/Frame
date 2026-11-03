#nullable enable

namespace Share.Conversation;

/// <summary>
/// 视图键词汇表（**跨引擎/UI 单源**，框架层）：键值只在此声明。
/// <para>作用域词与视图词的归一都经本类，禁各处再手写 "node"/"nodes"/"task" 字面量。</para>
/// </summary>
public static class ConversationViewKeys
{
    /// <summary>历史视图键（历史容器层）。</summary>
    public const string History = "history";

    /// <summary>任务视图键（子智能体对话层）。</summary>
    public const string Task = "task";

    /// <summary>节点视图键（结构节点层）。</summary>
    public const string Node = "node";

    /// <summary>区域读键（非视图，仅供读计划键空间使用）。</summary>
    public const string Region = "region";

    /// <summary>标准作用域词 → 视图键（未知 ⇒ <see cref="Node"/>，保持读计划既有缺省）。</summary>
    public static string FromScope(string? scope)
        => scope?.Trim().ToLowerInvariant() switch
        {
            Task => Task,
            Node or "nodes" => Node,
            History => History,
            Region => Region,
            _ => Node,
        };

    /// <summary>视图词（消息桥 data-type）→ 视图键（未知 ⇒ <see cref="History"/>）。</summary>
    public static string FromViewToken(string? token)
        => token?.Trim().ToLowerInvariant() switch
        {
            Task => Task,
            Node or "nodes" => Node,
            _ => History,
        };
}
