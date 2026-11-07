#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>边框模型投影结果（模型 + 解析期可见化问题）。</summary>
/// <param name="Spec">归一化后的边框模型。</param>
/// <param name="Issues">问题清单（<c>border-*</c>；空 = 无问题）。</param>
public sealed record BorderSpecProjectionResult(BorderSpec Spec, IReadOnlyList<SurfaceIssue> Issues);

/// <summary>
/// 边框模型适配层（**唯一 ResolvedMaterial → BorderSpec 入口**）：只搬运层视图，语义归一化在 Plate。
/// <para>同时实现 <c>I3 不可遮挡</c> 与 CSS 单 border 降级记账，供安装期落日志。</para>
/// </summary>
public static class BorderSpecProjection
{
    /// <summary>材质 → 边框模型（不抛；问题由 <see cref="Inspect"/> 返回）。</summary>
    /// <param name="material">已解析材质。</param>
    public static BorderSpec From(ResolvedMaterial material) => Inspect(material).Spec;

    /// <summary>材质 → 边框模型 + 解析期问题。</summary>
    /// <param name="material">已解析材质。</param>
    public static BorderSpecProjectionResult Inspect(ResolvedMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var inputs = new BorderLayerInput[material.Layers.Count];
        for (var index = 0; index < material.Layers.Count; index++)
        {
            var layer = material.Layers[index];
            inputs[index] = new BorderLayerInput(layer.Role, layer.Primitive, layer.Z, layer.Params);
        }

        var spec = BorderSpec.FromLayers(inputs, out var borderIssues);
        var issues = new List<SurfaceIssue>(borderIssues.Count);
        foreach (var issue in borderIssues) issues.Add(new SurfaceIssue(issue.Code, issue.Detail));

        if (spec.Strokes.Count > 0)
        {
            var maxStrokeZ = spec.Strokes.Max(stroke => stroke.Z);
            foreach (var layer in material.Layers)
            {
                if (layer.Z <= maxStrokeZ || !IsOccluder(layer)) continue;
                issues.Add(new SurfaceIssue(
                    "border-occluded",
                    string.Format("{0}:{1}:z={2}>{3}", layer.Primitive, layer.Role, layer.Z, maxStrokeZ)));
            }
        }

        if (spec.Strokes.Count > 1)
        {
            // CSS 单元素只能一条 border：多描边必须显式 emulated，不接受静默丢层。
            issues.Add(new SurfaceIssue(
                "border-multi-stroke-emulated",
                spec.Strokes.Count + " strokes; css keeps one border"));
        }

        return new BorderSpecProjectionResult(spec, issues);
    }

    /// <summary>是否比描边更靠前、且会压掉边的层（I3）。</summary>
    private static bool IsOccluder(ResolvedLayer layer)
    {
        switch (layer.Primitive)
        {
            case "fill":
            case "gradient":
            case "texture":
                return AlphaOf(layer) > 0;
            case "shadow":
                // 前景阴影（z > stroke z）按遮挡记账：bubble_glass 的 shadow(z=3) > edge(z=2) 即此例。
                return true;
            default:
                return false;
        }
    }

    private static int AlphaOf(ResolvedLayer layer)
    {
        var raw = layer.Params.TryGetValue("value", out var value) ? value : string.Empty;
        if (string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal))
        {
            var first = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (first.Length > 0) raw = first[0];
        }

        // 未解析值按不透明记账（保守：宁可报遮挡，不静默放过）。
        return SurfaceColor.TryParseColor(raw, out var argb) ? (argb >> 24) & 0xFF : 255;
    }
}
