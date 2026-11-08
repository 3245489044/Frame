#nullable enable

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 不可承载条目（**统一键空间**）：同一事实只此一种表示，禁各后端各用字符串拼接。
/// <para><see cref="Bind"/> 取值：<c>missing-structure</c>（该载体缺元素映射）、
/// <c>token-missing</c>（声明缺 token 值）、<c>unsupported-prop</c>、或原始属性名。</para>
/// </summary>
/// <param name="Carrier">载体 id。</param>
/// <param name="Part">渲染单元/角色键。</param>
/// <param name="Slot">槽位名（无则空串）。</param>
/// <param name="Bind">缺口原因/属性名。</param>
public readonly record struct UnsupportedSlot(string Carrier, string Part, string Slot, string Bind)
{
    /// <summary>可见化文本（诊断/日志）。</summary>
    public override string ToString() => $"{Carrier}:{Part}.{Slot}:{Bind}";
}