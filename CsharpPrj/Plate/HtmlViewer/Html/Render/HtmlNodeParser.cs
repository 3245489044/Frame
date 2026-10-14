#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// HTML → <see cref="UiNode"/> 投影器（L3 通用投影，零领域词汇）——把渲染层既有的 HTML 片段升级为键控节点树，
/// 供 <see cref="IUiTreeChannel"/>（键控对齐原语）作为写输入。容忍不闭合标签/注释/doctype/自闭合/空元素。
/// <para>产物根 = 片段节点（<see cref="UiNode.Tag"/> 为空串，子节点 = 顶层节点）。稳定键经 <see cref="HtmlKey.NodeKey"/> 派生；
/// &lt;details open&gt; → <see cref="UiControlState.Open"/>=true。</para>
/// </summary>
public static class HtmlNodeParser
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>解析 HTML 片段为键控节点树（片段根）。</summary>
    public static UiNode Parse(string? html)
    {
        var root = new Builder(null, null, null);
        if (string.IsNullOrEmpty(html)) return new UiNode(string.Empty, Children: Array.Empty<UiNode>());

        var stack = new Stack<Builder>();
        stack.Push(root);
        var text = new StringBuilder();
        int i = 0, n = html.Length;
        while (i < n)
        {
            if (html[i] != '<') { text.Append(html[i]); i++; continue; }

            // 注释 / doctype / CDATA：整段跳过。
            if (i + 1 < n && html[i + 1] == '!')
            {
                FlushText(stack, text);
                if (i + 3 < n && html[i + 2] == '-' && html[i + 3] == '-')
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                }
                else
                {
                    var gt = html.IndexOf('>', i);
                    i = gt < 0 ? n : gt + 1;
                }
                continue;
            }

            // 结束标签。
            if (i + 1 < n && html[i + 1] == '/')
            {
                FlushText(stack, text);
                var gt = html.IndexOf('>', i);
                if (gt < 0) { i = n; continue; }
                CloseTag(stack, html.Substring(i + 2, gt - (i + 2)).Trim());
                i = gt + 1;
                continue;
            }

            // 起始标签：读标签名。
            int j = i + 1;
            var tagSb = new StringBuilder();
            while (j < n && (char.IsLetterOrDigit(html[j]) || html[j] == '-' || html[j] == ':')) { tagSb.Append(html[j]); j++; }
            if (tagSb.Length == 0) { text.Append(html[i]); i++; continue; }

            FlushText(stack, text);
            var tag = tagSb.ToString();
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool selfClose = false;
            while (j < n && html[j] != '>')
            {
                while (j < n && char.IsWhiteSpace(html[j])) j++;
                if (j >= n || html[j] == '>') break;
                if (html[j] == '/') { selfClose = true; j++; continue; }

                var nameSb = new StringBuilder();
                while (j < n && html[j] != '=' && html[j] != '>' && !char.IsWhiteSpace(html[j]) && html[j] != '/')
                { nameSb.Append(html[j]); j++; }
                var attrName = nameSb.ToString();
                while (j < n && char.IsWhiteSpace(html[j])) j++;

                var value = string.Empty;
                if (j < n && html[j] == '=')
                {
                    j++;
                    while (j < n && char.IsWhiteSpace(html[j])) j++;
                    if (j < n && (html[j] == '"' || html[j] == '\''))
                    {
                        var q = html[j]; j++;
                        var start = j;
                        while (j < n && html[j] != q) j++;
                        value = html.Substring(start, j - start);
                        if (j < n) j++;
                    }
                    else
                    {
                        var start = j;
                        while (j < n && !char.IsWhiteSpace(html[j]) && html[j] != '>') j++;
                        value = html.Substring(start, j - start);
                    }
                }
                if (attrName.Length > 0) attrs[attrName] = value;
            }
            if (j < n && html[j] == '>') j++;
            i = j;

            var builder = new Builder(tag, attrs, null);
            stack.Peek().Children.Add(builder);
            if (!selfClose && !VoidElements.Contains(tag)) stack.Push(builder);
        }
        FlushText(stack, text);
        return root.ToNode();
    }

    private static void FlushText(Stack<Builder> stack, StringBuilder text)
    {
        if (text.Length == 0) return;
        var s = text.ToString();
        text.Clear();
        // 丢弃纯排版空白；但 <pre>/<textarea> 内的空白是内容，须保真。
        if (string.IsNullOrWhiteSpace(s) && !PreserveWhitespace(stack.Peek().Tag)) return;
        stack.Peek().Children.Add(new Builder(null, null, s));
    }

    private static bool PreserveWhitespace(string? tag)
        => string.Equals(tag, "pre", StringComparison.OrdinalIgnoreCase)
           || string.Equals(tag, "textarea", StringComparison.OrdinalIgnoreCase);

    private static void CloseTag(Stack<Builder> stack, string name)
    {
        if (name.Length == 0) return;
        var saved = new Stack<Builder>();
        while (stack.Count > 1)
        {
            var top = stack.Peek();
            if (string.Equals(top.Tag, name, StringComparison.OrdinalIgnoreCase)) { stack.Pop(); return; }
            saved.Push(stack.Pop());
        }
        while (saved.Count > 0) stack.Push(saved.Pop()); // 容忍：未匹配结束标签不破坏栈
    }

    private sealed class Builder
    {
        public Builder(string? tag, IReadOnlyDictionary<string, string>? attrs, string? text)
        {
            Tag = tag;
            Attrs = attrs;
            Text = text;
        }

        public string? Tag { get; }
        public IReadOnlyDictionary<string, string>? Attrs { get; }
        public string? Text { get; }
        public List<Builder> Children { get; } = new();

        public UiNode ToNode()
        {
            if (Tag is null)
            {
                // 片段根 / 混合容器：有子节点 ⇒ 片段节点；否则文本节点。
                return Children.Count > 0
                    ? new UiNode(string.Empty, Children: Children.ConvertAll(b => b.ToNode()))
                    : new UiNode(string.Empty, Text: Text);
            }
            var children = Children.Count == 0 ? null : Children.ConvertAll(b => b.ToNode());
            UiControlState? controlled = null;
            if (string.Equals(Tag, "details", StringComparison.OrdinalIgnoreCase)
                && Attrs is not null && Attrs.ContainsKey("open"))
                controlled = new UiControlState(Open: true);
            var key = HtmlKey.NodeKey.Derive(Attrs);
            var text = children is null ? Text : null;
            return new UiNode(Tag, key, Attrs, text, children, controlled);
        }
    }
}
