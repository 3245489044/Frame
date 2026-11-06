#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
                string.Join(" ", classes.Distinct(StringComparer.Ordinal)),
                attributes);
        }

        return map;
    }
}
