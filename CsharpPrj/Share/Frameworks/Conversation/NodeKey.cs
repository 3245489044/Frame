namespace Share.Conversation;

/// <summary>
/// 节点键拆分（**唯一实现**）—— 键约定 <c>wf.{wf}.{node}</c>；无前缀时 wf 为空。
/// 消费方：上下文绑定校验（R5 可解析判定）、UI 视图服务（剥离 wf 前缀）等，禁各地复刻同构实现。
/// </summary>
public static class NodeKey
{
    /// <summary>拆分节点键为 (wf, node)（<c>wf.nodeKey</c> → (<c>wf</c>,<c>nodeKey</c>)；无前缀 → ("", key)）。</summary>
    /// <param name="key">节点键（null → 空串）。</param>
    public static (string Wf, string Node) Split(string? key)
    {
        var value = key ?? string.Empty;
        var dot = value.IndexOf('.');
        return dot > 0 ? (value.Substring(0, dot), value.Substring(dot + 1)) : (string.Empty, value);
    }

    /// <summary>剥离 wf 前缀（<c>wf.nodeKey</c> → <c>nodeKey</c>；无前缀原样返回）。</summary>
    /// <param name="key">节点键（null → 空串）。</param>
    public static string StripPrefix(string? key) => Split(key).Node;
}