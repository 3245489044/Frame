namespace Strategy.Catalog.Prompt;

/// <summary>
/// 提示词档位（披露深度）—— Catalog 五 op 到提示词侧的映射粒度。
/// <para>
/// <see cref="Index"/> = 索引档（轴值 + 计数；常驻、浅）；
/// <see cref="Group"/> = 分组档（命中轴值下的条目摘要）；
/// <see cref="Entry"/> = 明细档（单条完整契约，仅按需展开）。
/// </para>
/// <para>纪律：索引档不得携带明细（有界量化；对齐 <c>CatalogTier</c> 的分级纪律）。</para>
/// </summary>
public enum TierDepth
{
    /// <summary>索引档（常驻；轴值 + 计数）。</summary>
    Index = 0,

    /// <summary>分组档（命中相关轴值才展开；条目摘要行）。</summary>
    Group = 1,

    /// <summary>明细档（仅按需工具调用展开；单条完整信息）。</summary>
    Entry = 2,
}
