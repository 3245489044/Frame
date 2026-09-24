namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 会话消息节点键的**单一权威**（对齐 <see cref="NodeTypes.ConversationMessage"/> 约定）：
/// <c>session.{name}.msg.{seq}</c>，<c>seq</c> 为 round_sequence 时序链序号（升序 = 时序权威）。
/// <para>键格式只此一处生成/解析，禁各处手拼（消除「键格式漂移 → 会话子图投影取不到节点」）。</para>
/// </summary>
public static class SessionMessageKeys
{
    /// <summary>会话消息节点键前缀（<c>session.</c>）。</summary>
    public const string Prefix = "session.";

    /// <summary>消息段标记（<c>.msg.</c>）。</summary>
    public const string MessageInfix = ".msg.";

    /// <summary>构造会话消息节点键：<c>session.{name}.msg.{seq}</c>（name/seq 均须非空）。</summary>
    public static string Build(string sessionName, int seq)
        => $"{Prefix}{sessionName}{MessageInfix}{seq}";

    /// <summary>是否为会话消息节点键（前缀 + 含消息段标记）。</summary>
    public static bool IsMessageKey(string? nodeKey)
        => !string.IsNullOrWhiteSpace(nodeKey)
           && nodeKey!.StartsWith(Prefix, System.StringComparison.Ordinal)
           && nodeKey.Contains(MessageInfix, System.StringComparison.Ordinal);

    /// <summary>解析时序序号（末尾整数段）；无 seq 段/不可解析 → 0（稳定序，不抛）。</summary>
    public static int ParseSeq(string? nodeKey)
    {
        if (!IsMessageKey(nodeKey)) return 0;
        var index = nodeKey!.LastIndexOf(MessageInfix, System.StringComparison.Ordinal) + MessageInfix.Length;
        return int.TryParse(nodeKey.Substring(index), out var seq) ? seq : 0;
    }
}
