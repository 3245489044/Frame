#nullable enable

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
/// <para>段键唯一来源 = <see cref="ConversationSectionPartitioner"/>；投影只有一条路径（声明不可用 ⇒ 可见降级）。</para>
/// </summary>
internal static class SurfaceFragmentPlanner
{
    /// <summary>声明不可用诊断码（逐段可见降级，禁静默回退旧投影器）。</summary>
    public const string SurfaceUnavailableCode = "surface-declaration-unavailable";

    /// <summary>按段产出片段（声明不可用 ⇒ 逐段可见降级，段键保持不变）。</summary>
    public static IReadOnlyList<ConversationRenderFragment> Plan(
        IReadOnlyList<ConversationSection> sections,
        ConversationProjectionContext projection,
        SurfaceDeclarations? declarations,
        string? viewKey)
    {
        var fragments = new List<ConversationRenderFragment>(sections.Count);
        if (declarations is null)
        {
            foreach (var section in sections)
            {
                fragments.Add(new ConversationRenderFragment(
                    section.Key, UiFragmentWriter.Fragment(Notices.Degraded(SurfaceUnavailableCode, projection))));
            }

            return fragments;
        }

        var backend = new HtmlSurfaceBackend(declarations.Map, "html", declarations.Ui, surface: declarations.Resolved);

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
                        backend, section.Items, projection, declarations, viewKey)));
                    break;

                case ConversationSectionKind.Operation:
                    fragments.Add(new ConversationRenderFragment(section.Key, Project(
                        backend, section.Items, projection, declarations, viewKey)));
                    break;

                case ConversationSectionKind.Round:
                    fragments.Add(new ConversationRenderFragment(section.Key, Project(
                        backend, section.Items, projection, declarations, viewKey)));
                    break;
            }
        }

        return fragments;
    }

    private static UiNode Project(
        HtmlSurfaceBackend backend,
        IReadOnlyList<ConversationRenderItem> items,
        ConversationProjectionContext projection,
        SurfaceDeclarations declarations,
        string? viewKey)
    {
        var plan = SurfaceRenderPipeline.Compose(items, projection.Policy, declarations, viewKey ?? string.Empty);
        return backend.Project(plan);
    }
}
