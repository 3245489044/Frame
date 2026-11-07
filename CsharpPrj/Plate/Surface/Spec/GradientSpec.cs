#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend;

/// <summary>
/// 单个渐变色标。<paramref name="Position"/> 仅在 <paramref name="HasPosition"/> 为真时有效（0..1）。
/// </summary>
/// <param name="Argb">打包 ARGB（<c>0xAARRGGBB</c>）。</param>
/// <param name="Position">归一化位置（0 = 起点，1 = 终点）。</param>
/// <param name="HasPosition">声明是否带位置。</param>
public readonly record struct GradientStop(int Argb, float Position, bool HasPosition);

/// <summary>
/// 渐变 stop 列表（强类型）。声明形态：<c>#111,#222</c> 或 <c>#111 0%,#222 100%</c>（<c>,</c> / <c>;</c> 均可分隔）。
/// <para>未声明位置 ⇒ 均匀分布（<see cref="HasPositions"/> = false），保持既有载体输出形态。</para>
/// </summary>
public sealed record GradientSpec(IReadOnlyList<GradientStop> Stops)
{
    /// <summary>空渐变（无 stop）。</summary>
    public static readonly GradientSpec Empty = new(Array.Empty<GradientStop>());

    /// <summary>是否至少一个 stop 带显式位置。</summary>
    public bool HasPositions
    {
        get
        {
            foreach (var stop in Stops)
            {
                if (stop.HasPosition) return true;
            }

            return false;
        }
    }

    /// <summary>stop 色（顺序即声明序）。</summary>
    public IReadOnlyList<int> Colors
    {
        get
        {
            var colors = new int[Stops.Count];
            for (var i = 0; i < Stops.Count; i++) colors[i] = Stops[i].Argb;
            return colors;
        }
    }

    /// <summary>
    /// 解析 <c>value</c>：逐项色值（可带位置），任一项非法 ⇒ 空渐变 + 差错登记（禁部分落地）。
    /// </summary>
    /// <param name="raw">原始 <c>value</c> 文本。</param>
    /// <param name="role">层角色（差错定位用）。</param>
    /// <param name="issues">差错收集器。</param>
    public static GradientSpec Parse(string? raw, string role, IList<LayerIssue> issues)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            issues.Add(new LayerIssue("param-missing", role + ":value=" + text));
            return Empty;
        }

        var parts = LayerParams.SplitList(text);
        if (parts.Length == 0)
        {
            issues.Add(new LayerIssue("param-missing", role + ":value=" + text));
            return Empty;
        }

        var stops = new List<GradientStop>(parts.Length);
        var failed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var tokens = LayerParams.SplitTokens(parts[i]);
            if (tokens.Length == 0)
            {
                issues.Add(new LayerIssue("color-invalid", $"{role}:value[{i}]={parts[i]}"));
                failed = true;
                continue;
            }

            if (!SurfaceColor.TryParseColor(tokens[0], out var argb))
            {
                issues.Add(new LayerIssue("color-invalid", $"{role}:value[{i}]={parts[i]}"));
                failed = true;
                continue;
            }

            var position = 0f;
            var hasPosition = false;
            if (tokens.Length > 1)
            {
                var positionText = tokens[1];
                if (positionText.EndsWith("%", StringComparison.Ordinal))
                {
                    positionText = positionText[..^1];
                }

                if (!LayerParams.TryFloat(positionText, out var parsed))
                {
                    issues.Add(new LayerIssue("param-invalid", $"{role}:value[{i}]={parts[i]}"));
                    failed = true;
                    continue;
                }

                position = LayerParams.Clamp01(parsed > 1f ? parsed / 100f : parsed);
                hasPosition = true;
            }

            stops.Add(new GradientStop(argb, position, hasPosition));
        }

        // 任一项非法 ⇒ 整层不落地（禁部分渐变），但问题要一次列全（K1：解析期一次产判据）。
        return failed ? Empty : new GradientSpec(stops);
    }
}
