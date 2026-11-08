#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 渲染计划指纹（**可复算**）：<see cref="RenderPlan.Roots"/> 的稳定序列化 → SHA256。
/// <para>用途：R2c「同输入 ⇒ 同节点树」对拍；键序/字段序固定，禁把执行者叙述当判据。</para>
/// </summary>
public static class RenderPlanFingerprint
{
    /// <summary>计算计划指纹（空计划 = <c>empty</c>）。</summary>
    /// <param name="plan">渲染计划。</param>
    public static string Of(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Roots.Count == 0) return "empty";

        var sb = new StringBuilder(plan.Roots.Count * 128);
        sb.Append("view=").Append(plan.ViewKey).Append('\n');
        foreach (var root in plan.Roots)
        {
            AppendNode(sb, root, 0);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AppendNode(StringBuilder sb, UiSurfaceNode node, int depth)
    {
        sb.Append(' ', depth * 2).Append("node|")
            .Append(node.Role).Append('|')
            .Append(node.Key).Append('|')
            .Append(node.Surface.ToString()).Append('|')
            .Append(node.Class).Append('|')
            .Append(node.State.ToString()).Append('|')
            .Append(node.Actions is { Count: > 0 } actions ? string.Join(",", actions) : string.Empty)
            .Append('\n');

        foreach (var field in node.Fields)
        {
            sb.Append(' ', depth * 2 + 2).Append("field|")
                .Append(field.Name).Append('|')
                .Append(field.Cell).Append('|')
                .Append(field.Visibility.ToString()).Append('|')
                .Append(field.UnknownReason ?? string.Empty).Append('|')
                .Append(Canonical(field.Value))
                .Append('\n');
        }

        foreach (var child in node.Children)
        {
            AppendNode(sb, child, depth + 1);
        }
    }

    private static string Canonical(object? value)
        => SurfaceKeyCanonical.CanonicalValue(value);
}
