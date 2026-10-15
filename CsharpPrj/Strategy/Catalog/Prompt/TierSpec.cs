using System;
using Strategy.Catalog.Core;

namespace Strategy.Catalog.Prompt;

/// <summary>
/// 提示词分级组声明（TierSpec）—— 一行接入一个提示词级目录面（新增分级组 = 加一行 <see cref="PromptTierCatalog.Register"/>）。
/// <para>
/// 机制与语义分离：<see cref="SourceId"/> / <see cref="AxisId"/> / <see cref="Key"/> 等语义值由上层注入，
/// 框架内零领域字符串（红线）。<see cref="BandOrder"/> 仅用于**段内** tier 行排序；
/// 段级顺序权威仍是 <c>Share.Conversation.CacheBand</c>（不在本层重复）。
/// </para>
/// </summary>
/// <param name="Key">分级组稳定键（全局唯一；供幂等注册与诊断）。</param>
/// <param name="UiStage">投影为头提示层气泡的 stage（前缀 <c>prompt:</c> ⇒ 折叠气泡）。</param>
/// <param name="SourceId">目录源 Id（<c>CatalogQueryService</c> 的 sourceId）。</param>
/// <param name="AxisId">投影所依据的轴 Id（索引档取该轴的目录行）。</param>
/// <param name="Project">纯投影函数：目录查询服务 → 档行文本（复用 CatalogGrouper，禁新增查询实现）。</param>
/// <param name="BandOrder">段内排序序号（升序；越小越靠前）。</param>
/// <param name="Depth">披露深度（缺省 <see cref="TierDepth.Index"/>）。</param>
public sealed record TierSpec(
    string Key,
    string UiStage,
    string SourceId,
    string AxisId,
    Func<CatalogQueryService, string> Project,
    int BandOrder,
    TierDepth Depth = TierDepth.Index);
