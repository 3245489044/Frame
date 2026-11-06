#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation;
using Share.Conversation.Rendering;
using Share.Conversation.Rendering.Surface;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>
/// 段计划（B2/M4b）：段划分 → 逐段合成 <see cref="RenderPlan"/> → 载体投影 → 片段。
/// <para>段键唯一来源 = <see cref="ConversationSectionPartitioner"/>；开关只切投影器，不切段键。</para>
/// </summary>
internal static class SurfaceFragmentPlanner
{
    /// <summary>按段产出片段（declarations 为空 ⇒ 走旧投影器，零回归）。</summary>
    public static IReadOnlyList<ConversationRenderFragment> Plan(
        IReadOnlyList<ConversationSection> sections,
        ConversationProjectionContext projection,
        SurfaceDeclarations? declarations,
        string? viewKey)
    {
        var fragments = new List<ConversationRenderFragment>(sections.Count);
        HtmlSurfaceBackend? backend = declarations is null
            ? null
            : new HtmlSurfaceBackend(declarations.Map, "html", declarations.Ui, surface: declarations.Resolved);
        var latestRound = sections.Where(section => section.Kind == ConversationSectionKind.Round)
            .Select(section => section.Round)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var section in sections)
        {
            switch (section.Kind)
            {
                case ConversationSectionKind.Degraded:
                    fragments.Add(new ConversationRenderFragment(
                        section.Key, UiFragmentWriter.Fragment(Notices.Degraded(section.Reason ?? string.Empty, projection))));
                    break;

                case ConversationSectionKind.Empty:
                    fragments.Add(new ConversationRenderFragment(
                        section.Key, UiFragmentWriter.Fragment(Notices.Empty(projection))));
                    break;

                case ConversationSectionKind.Head:
                    fragments.Add(new ConversationRenderFragment(section.Key, Project(
                        backend, section.Items, projection, declarations, viewKey,
                        () => UiFragmentWriter.Fragment(HeadBandProjector.Project(section.Items, projection)))));
                    break;

                case ConversationSectionKind.Operation:
                    fragments.Add(new ConversationRenderFragment(section.Key, Project(
                        backend, section.Items, projection, declarations, viewKey,
                        () => UiFragmentWriter.Fragment(OperationProjector.Project(section.Items, projection)))));
                    break;

                case ConversationSectionKind.Round:
                    fragments.Add(new ConversationRenderFragment(section.Key, Project(
                        backend, section.Items, projection, declarations, viewKey,
                        () => UiFragmentWriter.Fragment(RoundSectionProjector.Project(
                            section.Round, section.Items, latestRound, projection)))));
                    break;
            }
        }

        return fragments;
    }

    private static UiNode Project(
        HtmlSurfaceBackend? backend,
        IReadOnlyList<ConversationRenderItem> items,
        ConversationProjectionContext projection,
        SurfaceDeclarations? declarations,
        string? viewKey,
        Func<UiNode> legacy)
    {
        if (backend is null || declarations is null) return legacy();
        var plan = SurfaceRenderPipeline.Compose(items, projection.Policy, declarations, viewKey ?? string.Empty);
        return backend.Project(plan);
    }
}
