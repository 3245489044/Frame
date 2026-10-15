namespace Strategy.Catalog.Model;

/// <summary>
/// 目录分级（分级轴）—— 「新华字典式」查询的四个视图层级，不是条目的固有属性，而是**查询结果的粒度标识**：
/// <list type="number">
/// <item><see cref="Index"/>：一级索引（轴值 + 计数；如「部首表」），只给"有哪些分区、各多少条"，不给明细。</item>
/// <item><see cref="Group"/>：二级目录（某个轴取值下的条目摘要；如「氵部字表」）。</item>
/// <item><see cref="Entry"/>：条目实体（单条摘要行：键 + 显示名 + 一句话 + 执行体轨）。</item>
/// <item><see cref="Detail"/>：明细（单条的完整信息：字段/参数/契约/执行体轨；**不进索引**，按需展开）。</item>
/// </list>
/// <para>
/// 纪律（有界量化）：<see cref="Index"/> 层不得携带 <see cref="Detail"/> 内容，否则"分级展开"退化为
/// "把全量明细塞进一次响应"，正是上下文膨胀的结构性来源。
/// </para>
/// </summary>
public enum CatalogTier
{
    /// <summary>一级索引：轴值 + 计数（+ 常驻条目）。</summary>
    Index,

    /// <summary>二级目录：某轴取值下的条目集合（摘要行）。</summary>
    Group,

    /// <summary>条目实体：单条摘要（键 / 显示名 / 摘要 / 执行体轨）。</summary>
    Entry,

    /// <summary>明细：单条完整信息（字段集合；按需展开）。</summary>
    Detail,
}
