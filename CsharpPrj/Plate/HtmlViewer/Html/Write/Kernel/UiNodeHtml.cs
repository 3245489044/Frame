#nullable enable

using System.Collections.Generic;
using System.Text;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 键控树 → HTML 序列化（Bulk 原语载荷）：保留标签/属性（稳定键在属性里）/文本/子节点。
/// <para>仅用于 <see cref="SectionWriteMode.BulkReplace"/>（整节一次赋值）；不承担对齐语义（对齐仍由 applyTree 负责）。</para>
/// </summary>
public static class UiNodeHtml
{
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
            sb.Append(EscapeText(node.Text));
            return;
        }

        sb.Append('<').Append(node.Tag);
        if (node.Attributes is not null)
        {
            foreach (var kv in node.Attributes)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                sb.Append(' ').Append(kv.Key).Append("=\"").Append(EscapeAttr(kv.Value)).Append('"');
            }
        }

        sb.Append('>');
        if (node.Text is not null) sb.Append(EscapeText(node.Text));
        if (node.Children is not null)
        {
            foreach (var child in node.Children) Append(child, sb);
        }

        sb.Append("</").Append(node.Tag).Append('>');
    }

    private static string EscapeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeAttr(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
