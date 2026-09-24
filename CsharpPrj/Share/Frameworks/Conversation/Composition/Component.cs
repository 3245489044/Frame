namespace Share.Composition;

/// <summary>
/// 部件默认实现 —— 纯数据载体（<see cref="IComponent"/> 的最小构造）。
/// 文本部件的 Order 语义为「槽内升序」，与既有装配按序拼接语义一致（碎片顺序即语义）。
/// </summary>
/// <param name="Slot">槽位标识。</param>
/// <param name="Order">槽内顺序。</param>
/// <param name="Content">内容本体。</param>
public sealed record Component(string Slot, int Order, object? Content) : IComponent
{
    /// <summary>文本部件快捷构造（提示词段 / 模板片段等）。</summary>
    public static Component Text(string slot, int order, string? text)
        => new(slot, order, text ?? string.Empty);

    /// <summary>内容文本表示（null → 空串），供槽位装配器取值；取值语义权威 = <see cref="KernelJoins.TextOf"/>。</summary>
    public string TextValue => KernelJoins.TextOf(this);
}
