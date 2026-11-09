#nullable enable

using System;
using System.Collections.Generic;
using Modules.Parsing.Markup;
using Share.Conversation.Rendering.Surface;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>已解析表面 → Plate 中性结构标记（M4a 唯一转换点；Plate 不依赖 Share）。</summary>
public static class SurfaceMarkupProjection
{
    /// <summary>按容器键产出结构标记（未解析/无结构 ⇒ 空表）。</summary>
    public static IReadOnlyDictionary<string, SurfaceContainerMarkup> Build(ResolvedSurface? surface)
    {
        var map = new Dictionary<string, SurfaceContainerMarkup>(StringComparer.Ordinal);
        if (surface is null) return map;

        foreach (var container in surface.Containers)
        {
            var tag = container.Structure.TryGetValue("tag", out var rawTag) && rawTag.Length > 0 ? rawTag : "div";
            var classes = new List<string>();
            if (container.Structure.TryGetValue("cls", out var cls) && cls.Length > 0) classes.Add(cls);
            foreach (var material in container.Materials.Values)
            {
                classes.Add(CssSurfaceProjector.ClassName(material.FallbackUsed ?? material.MaterialId));
            }

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (container.Structure.TryGetValue("attrs", out var rawAttrs) && rawAttrs.Length > 0)
            {
                foreach (var pair in rawAttrs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var split = pair.Split('=', 2);
                    if (split.Length == 2) attributes[split[0]] = split[1];
                }
            }

            map[container.Key] = new SurfaceContainerMarkup(
                tag,
                MarkupClass.JoinDistinct(classes.ToArray()),
                attributes);
        }

        return map;
    }

    /// <summary>
    /// 语义槽 → 当前档位实际材质的 CSS 类名（元素级材质下沉的唯一取值入口）。
    /// <para>元素只声明语义槽（<c>panel_surface</c> / <c>row_surface</c>），材质 id 由当前档位解析；
    /// 找不到 ⇒ 空串（调用方保持原类名，不静默套错材质）。</para>
    /// </summary>
    public static string ClassForSlot(ResolvedSurface? surface, string slot)
    {
        if (surface is null || string.IsNullOrWhiteSpace(slot)) return string.Empty;
        foreach (var container in surface.Containers)
        {
            if (container.Materials.TryGetValue(slot, out var material))
            {
                return CssSurfaceProjector.ClassName(material.FallbackUsed ?? material.MaterialId);
            }
        }

        return string.Empty;
    }
}
