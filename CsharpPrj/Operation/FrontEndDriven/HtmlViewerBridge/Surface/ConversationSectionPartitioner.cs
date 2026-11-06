#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>段种类（段键的唯一来源；新增面 = 加一种 + 一处投影）。</summary>
internal enum ConversationSectionKind
{
    Degraded,
    Empty,
    Head,
    Operation,
    Round,
}

/// <summary>一个渲染段（段键 + 段内项 + 轮次信息）。</summary>
internal sealed record ConversationSection(
    string Key,
    ConversationSectionKind Kind,
    IReadOnlyList<ConversationRenderItem> Items,
    string? Reason = null,
    int Round = 0,
    bool LatestRound = false);

/// <summary>
/// 段划分（**唯一实现**）：面序 + 通道分流 + 头提示前置 + 轮分组 + 降级/空态。
/// <para>段键规则冻结在此：<c>degraded:&lt;reason&gt; / empty / head / op / round:&lt;n&gt;</c>。</para>
/// <para>旧字符串投影器与新表面管线共用本划分，保证切换时段键逐字相等。</para>
/// </summary>
internal static class ConversationSectionPartitioner
{
    /// <summary>按声明面序划分段（顺序 = 最终片段顺序）。</summary>
    public static IReadOnlyList<ConversationSection> Partition(
        IReadOnlyList<ConversationRenderItem> items,
        ConversationProjectionContext projection,
        IReadOnlyList<string> degradations,
        HtmlStreamRendererOptions options,
        PresentationRegions regions)
    {
        var sections = new List<ConversationSection>(8);
        foreach (var reason in degradations)
        {
            sections.Add(new ConversationSection(
                "degraded:" + reason, ConversationSectionKind.Degraded, Array.Empty<ConversationRenderItem>(), reason));
        }

        if (items is null || items.Count == 0)
        {
            sections.Add(new ConversationSection("empty", ConversationSectionKind.Empty, Array.Empty<ConversationRenderItem>()));
            return sections;
        }

        IReadOnlyList<ConversationRenderItem> dialogue = items;
        IReadOnlyList<ConversationRenderItem> operations = Array.Empty<ConversationRenderItem>();
        if (options.SplitOperationChannel)
        {
            var operationSplit = items.Where(projection.IsOperation).ToList();
            if (operationSplit.Count > 0)
            {
                operations = operationSplit;
                dialogue = items.Where(item => !projection.IsOperation(item)).ToList();
            }
        }

        IReadOnlyList<ConversationRenderItem> head = Array.Empty<ConversationRenderItem>();
        if (options.HeadBandOrdering)
        {
            head = dialogue.Where(projection.IsPrompt).ToList();
            if (head.Count > 0)
            {
                dialogue = dialogue.Where(item => !projection.IsPrompt(item)).ToList();
            }
        }

        var rounds = dialogue.GroupBy(item => item.Round).OrderBy(group => group.Key).ToList();
        var latestRound = rounds.Count > 0 ? rounds[^1].Key : 0;

        foreach (var renderSurface in options.SurfaceOrder ?? RenderSurfaceCatalog.OrderOf(regions))
        {
            switch (renderSurface)
            {
                case RenderSurface.HeadPrompt:
                    if (head.Count > 0)
                    {
                        sections.Add(new ConversationSection("head", ConversationSectionKind.Head, head));
                    }

                    break;

                case RenderSurface.Operation:
                    if (operations.Count > 0)
                    {
                        sections.Add(new ConversationSection("op", ConversationSectionKind.Operation, operations));
                    }

                    break;

                case RenderSurface.Dialogue:
                    foreach (var round in rounds)
                    {
                        sections.Add(new ConversationSection(
                            FragmentKeys.RoundSection(round.Key),
                            ConversationSectionKind.Round,
                            round.ToList(),
                            Round: round.Key,
                            LatestRound: round.Key == latestRound));
                    }

                    break;
            }
        }

        return sections;
    }
}
