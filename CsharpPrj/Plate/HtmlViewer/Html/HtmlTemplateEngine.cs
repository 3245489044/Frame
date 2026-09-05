#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 轻量 JSON 模板引擎 —— 把「JSON 模板 + 行数据模型」渲染为 HTML 片段，支撑「HTML 写进 JSON、C# 只供数据」。
/// P5 定位：<b>安全 fallback</b>（无 JS 场景 / 单元测试）——常规渲染可由页面自带 JS 承担（C# 仅把结构化数据 JSON 交给 window 渲染器）；本引擎保留为无 JS/单测兜底。
/// <list type="bullet">
/// <item><c>{field}</c> — HTML 编码绑定（默认，XSS 安全，延续 HtmlEncode 纪律）</item>
/// <item><c>{field|raw}</c> — 受信原文绑定（仅用于已渲染的内层片段，如 {tree|raw}/{history|raw}）</item>
/// <item><c>{#each list}…{/each}</c> — 列表循环（行级作用域，可嵌套，如 history → tasks → taskItem）</item>
/// <item><c>{?cond}A{:else}B{/}</c> — 条件分支（cond 非空为真，如 active/open/tool 位）</item>
/// <item><c>{&gt;name}</c> — 调用命名模板（以当前模型渲染）</item>
/// </list>
/// 纯字符串替换 + 编码，不执行任意 JS（对齐可信通道/白名单安全姿态）；未知绑定渲染为空；未配对块容错跳过。
/// 模板不缓存（片段小、渲染非热路径），解析一次渲染一次。
/// </summary>
public static class HtmlTemplateEngine
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyModel =
        new Dictionary<string, object?>();

    /// <summary>渲染命名模板（模板缺失/空 → fallback）。</summary>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?>? model,
        string fallback = "")
    {
        if (templates is null || !templates.TryGetValue(name, out var tpl) || string.IsNullOrWhiteSpace(tpl))
            return fallback;
        return RenderTemplate(tpl, templates, model);
    }

    /// <summary>渲染一段模板字符串（<paramref name="templates"/> 供 {&gt;name} 命名模板调用解析，可 null）。</summary>
    public static string RenderTemplate(
        string template,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?>? model)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        int i = 0;
        var nodes = ParseNodes(template, ref i, out _);
        var sb = new StringBuilder(template.Length);
        RenderNodes(nodes, templates, model ?? EmptyModel, sb);
        return sb.ToString();
    }

    // ---- 节点模型 ----
    private abstract record Node;
    private sealed record TextNode(string Text) : Node;
    private sealed record BindingNode(string Field, bool Raw) : Node;
    private sealed record EachNode(string ListName, List<Node> Children) : Node;
    private sealed record CondNode(string Cond, List<Node> If, List<Node> Else) : Node;
    private sealed record NamedNode(string Name) : Node;

    // ---- 解析：模板 → 节点树（stop = "/" 遇到 {/}，="else" 遇到 {:else}，="" 到结尾；i 按 ref 传入以共享游标）----
    private static List<Node> ParseNodes(string tpl, ref int i, out string stop)
    {
        var nodes = new List<Node>();
        int n = tpl.Length;
        while (i < n)
        {
            int open = tpl.IndexOf('{', i);
            if (open < 0) { nodes.Add(new TextNode(tpl.Substring(i))); i = n; break; }
            if (open > i) nodes.Add(new TextNode(tpl.Substring(i, open - i)));
            int close = tpl.IndexOf('}', open);
            if (close < 0) { nodes.Add(new TextNode(tpl.Substring(open))); i = n; break; }
            string token = tpl.Substring(open + 1, close - open - 1).Trim();
            i = close + 1;

            if (token.Length == 0) { nodes.Add(new TextNode("{}")); continue; }
            char first = token[0];

            if (first == '/')
            {
                stop = "/";   // {/} 或 {/each name} → 结束本层
                return nodes;
            }
            if (first == ':')
            {
                stop = "else"; // {:else}
                return nodes;
            }
            if (first == '#')
            {
                // 语法 {#each list}：token 形如 "#each items"，剥去 "#each" 关键字后取列表名
                var listName = token.Length > 5 ? token[5..].Trim() : string.Empty;
                var children = ParseNodes(tpl, ref i, out _);
                nodes.Add(new EachNode(listName, children));
            }
            else if (first == '?')
            {
                var ifNodes = ParseNodes(tpl, ref i, out var stop0);
                var elseNodes = new List<Node>();
                if (stop0 == "else") { elseNodes = ParseNodes(tpl, ref i, out _); }
                nodes.Add(new CondNode(token[1..].Trim(), ifNodes, elseNodes));
            }
            else if (first == '>')
            {
                nodes.Add(new NamedNode(token[1..].Trim()));
            }
            else
            {
                string field = token;
                bool raw = false;
                int pipe = token.IndexOf('|');
                if (pipe >= 0) { field = token[..pipe].Trim(); raw = true; }
                nodes.Add(new BindingNode(field, raw));
            }
        }
        stop = "";
        return nodes;
    }


    // ---- 渲染 ----
    private static void RenderNodes(
        List<Node> nodes,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?> model,
        StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    sb.Append(t.Text);
                    break;
                case BindingNode b:
                    var val = Get(model, b.Field);
                    if (val is null) break;
                    sb.Append(b.Raw ? val : WebUtility.HtmlEncode(val));
                    break;
                case EachNode e:
                    if (TryGetList(model, e.ListName, out var list))
                    {
                        foreach (var item in list)
                            RenderNodes(e.Children, templates, ToModel(item), sb);
                    }
                    break;
                case CondNode c:
                    RenderNodes(EvalCond(model, c.Cond) ? c.If : c.Else, templates, model, sb);
                    break;
                case NamedNode nn:
                    if (templates is not null && templates.TryGetValue(nn.Name, out var childTpl))
                    {
                        int j = 0;
                        RenderNodes(ParseNodes(childTpl, ref j, out _), templates, model, sb);
                    }
                    break;
            }
        }
    }

    /// <summary>条件求值：`{?field=value}` 等式比较（大小写不敏感）——让模板按字段原始值承担分支（如 `{?role=user}`）；
    /// 否则按既有语义：字段非空即真。</summary>
    private static bool EvalCond(IReadOnlyDictionary<string, object?> model, string cond)
    {
        int eq = cond.IndexOf('=');
        if (eq > 0)
        {
            var field = cond[..eq].Trim();
            var expected = cond[(eq + 1)..].Trim();
            return string.Equals(Get(model, field), expected, StringComparison.OrdinalIgnoreCase);
        }
        var v = Get(model, cond);
        return v is not null && v.Length > 0;
    }

    private static IReadOnlyDictionary<string, object?> ToModel(object? item)
    {
        if (item is IReadOnlyDictionary<string, object?> dict) return dict;
        // 标量列表项（如 {#each nodes} 的字符串节点键）：暴露 {this} 指向该项本身，
        // 使「{#each list}<span>{this}</span>{/each}」可渲染纯字符串/标量列表（字典项走上方分支，行为不变）。
        if (item is not null)
            return new Dictionary<string, object?> { ["this"] = item };
        return EmptyModel;
    }

    private static bool TryGetList(
        IReadOnlyDictionary<string, object?> model,
        string name,
        out IEnumerable<object?> list)
    {
        foreach (var kv in model)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                if (kv.Value is IEnumerable<object?> seq) { list = seq; return true; }
                break;
            }
        }
        list = [];
        return false;
    }

    /// <summary>取值（大小写不敏感），返回字符串或 null。</summary>
    private static string? Get(IReadOnlyDictionary<string, object?> model, string field)
    {
        foreach (var kv in model)
        {
            if (string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
                return kv.Value?.ToString();
        }
        return null;
    }
}
