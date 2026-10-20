namespace Share.Messaging;

/// <summary>
/// 生命周期迁移表（唯一状态机，纯函数）：
/// <code>
/// Created → Published → Claimed → Consumed → Acked
///                ↘ Expired / Failed / Cancelled ↗
/// Failed → Published（重试） | DeadLetter
/// </code>
/// <para>终态（Acked/Expired/DeadLetter/Cancelled）不可出边（禁复活）。</para>
/// </summary>
public static class MessageStateMachine
{
    private static readonly IReadOnlyDictionary<MessageState, MessageState[]> Edges =
        new Dictionary<MessageState, MessageState[]>
        {
            [MessageState.Created] = new[] { MessageState.Published, MessageState.Cancelled },
            [MessageState.Published] = new[]
            {
                MessageState.Claimed, MessageState.Expired, MessageState.Failed, MessageState.Cancelled,
            },
            [MessageState.Claimed] = new[]
            {
                MessageState.Consumed, MessageState.Expired, MessageState.Failed, MessageState.Cancelled,
            },
            [MessageState.Consumed] = new[] { MessageState.Acked, MessageState.Failed },
            [MessageState.Failed] = new[] { MessageState.Published, MessageState.DeadLetter },
            [MessageState.Acked] = Array.Empty<MessageState>(),
            [MessageState.Expired] = Array.Empty<MessageState>(),
            [MessageState.DeadLetter] = Array.Empty<MessageState>(),
            [MessageState.Cancelled] = Array.Empty<MessageState>(),
        };

    /// <summary>是否为终态。</summary>
    public static bool IsTerminal(MessageState state)
        => state is MessageState.Acked or MessageState.Expired or MessageState.DeadLetter or MessageState.Cancelled;

    /// <summary>当前状态的合法后继（终态 ⇒ 空）。</summary>
    public static IReadOnlyList<MessageState> Next(MessageState state)
        => Edges.TryGetValue(state, out var next) ? next : Array.Empty<MessageState>();

    /// <summary>迁移是否合法。</summary>
    public static bool CanTransition(MessageState from, MessageState to)
        => Edges.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    /// <summary>迁移判定（失败给出可读原因；不抛）。</summary>
    public static bool TryTransition(MessageState from, MessageState to, out string reason)
    {
        if (from == to)
        {
            reason = $"状态未变化（{from}）。";
            return false;
        }

        if (IsTerminal(from))
        {
            reason = $"终态不可迁移（{from} → {to}）。";
            return false;
        }

        if (!CanTransition(from, to))
        {
            reason = $"非法迁移（{from} → {to}）。";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
