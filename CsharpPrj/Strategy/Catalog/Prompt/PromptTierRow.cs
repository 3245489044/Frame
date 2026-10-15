namespace Strategy.Catalog.Prompt;

/// <summary>
/// 提示词档行（PromptTierRow）—— 一个分级组投影出的**一行**段文本（供 UI/LLM 双向比对与 I3 哈希）。
/// </summary>
/// <param name="Depth">披露深度。</param>
/// <param name="Key">分级组键。</param>
/// <param name="Stage">展示 stage（<c>prompt:</c> 前缀）。</param>
/// <param name="Text">档行文本。</param>
/// <param name="BandOrder">段内排序序号。</param>
/// <param name="Tokens">文本体量粗估（token）。</param>
public sealed record PromptTierRow(
    TierDepth Depth,
    string Key,
    string Stage,
    string Text,
    int BandOrder,
    int Tokens);
