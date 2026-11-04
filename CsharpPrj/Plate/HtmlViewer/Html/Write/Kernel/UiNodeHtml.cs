#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Modules.Parsing.Markup;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 键控树 → HTML 序列化（Bulk 原语载荷）：保留标签/属性（稳定键在属性里）/文本/子节点。
/// <para>仅用于 <see cref="SectionWriteMode.BulkReplace"/>（整节一次赋值）；不承担对齐语义（对齐仍由 applyTree 负责）。</para>
/// <para>词表（空元素/布尔属性）与转义单源 = <see cref="MarkupTerms"/> / <see cref="MarkupDialects"/>。</para>
/// </summary>
public static class UiNodeHtml
{
    private static IReadOnlySet<string> VoidElements => MarkupTerms.VoidTags;

    private static IReadOnlySet<string> BooleanAttributes => MarkupTerms.BooleanAttributes;


    public static string ToHtml(UiNode? node)
    {
        if (node is null) return string.Empty;
        var sb = new StringBuilder();
        Append(node, sb);
        return sb.ToString();
    }

    private static void Append(UiNode node, StringBuilder sb)
    {
        if (node.IsText)
        {
            // 片段根（Tag=="" 且带子节点）：与页面 __uiReconcile.appendInto 同语义，按容器透传子节点。
            // 曾把片段根当纯文本 ⇒ Bulk 载荷恒为空串 ⇒ bulk(innerHTML="") 清空整个区域（F-1/F-2 根因）。
            if (node.Children is { Count: > 0 })
            {
                foreach (var child in node.Children) Append(child, sb);
                return;
            }
            sb.Append(node.Raw ? node.Text ?? string.Empty : EscapeText(node.Text));
            return;
        }
        sb.Append('<').Append(node.Tag);
        if (node.Attributes is not null)
        {
            foreach (var kv in node.Attributes)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (string.IsNullOrEmpty(kv.Value) && BooleanAttributes.Contains(kv.Key)) sb.Append(' ').Append(kv.Key);
                else sb.Append(' ').Append(kv.Key).Append("=\"").Append(EscapeAttr(kv.Value)).Append('"');
            }
        }

        sb.Append('>');
        if (node.Text is not null) sb.Append(EscapeText(node.Text));
        if (node.Children is not null)
        {
            foreach (var child in node.Children) Append(child, sb);
        }

        if (!VoidElements.Contains(node.Tag)) sb.Append("</").Append(node.Tag).Append('>');
    }

    private static string EscapeText(string? text)
        => MarkupDialects.Html.EncodeText(text ?? string.Empty);

    private static string EscapeAttr(string? value)
        => MarkupDialects.Html.EncodeAttr(value ?? string.Empty);
}
