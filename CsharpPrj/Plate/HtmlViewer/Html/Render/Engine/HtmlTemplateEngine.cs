#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Modules.Parsing.Markup;
using UiBackend.Abstractions;

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
/// 模板整段解析结果按原文缓存（声明期常量，非热路径）；渲染不缓存。
/// </summary>
public static class HtmlTemplateEngine
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyModel =
        new Dictionary<string, object?>();

    /// <summary>模板整段解析缓存（模板为声明期常量；键 = 模板原文）。消除每次渲染/每次 {&gt;name} 调用的重复解析。</summary>
    private static readonly ConcurrentDictionary<string, List<Node>> ParseCache = new(StringComparer.Ordinal);

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

    /// <summary>渲染命名模板为**受信片段**（唯一包装点：字符串 → <see cref="UiMarkup"/>）。</summary>
    public static UiMarkup RenderMarkup(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?>? model,
        string fallback = "")
        => UiMarkup.Trusted(Render(templates, name, model, fallback));

    /// <summary>渲染一段模板字符串（<paramref name="templates"/> 供 {&gt;name} 命名模板调用解析，可 null）。</summary>
    public static string RenderTemplate(
        string template,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?>? model)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var sb = new StringBuilder(template.Length);
        RenderNodes(ParseWhole(template), templates, model ?? EmptyModel, sb);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染命名模板为**节点树**（与 <see cref="Render"/> 共用同一解析器与解析缓存，禁第二套模板解析）。
    /// <para>语义与 <see cref="Render"/> 逐条一致：文本/绑定 → 文本节点（序列化时统一转义）；
    /// <c>{#each}</c>/<c>{?}</c>/<c>{&gt;partial}</c> 同构展开。<c>|raw</c> 位由 <see cref="UiMarkup"/> 直接承载
    /// （受信片段不二次转义，裸 string fail-fast）⇒ 模板 → 节点一次成型，无 HTML→节点回解析。</para>
    /// </summary>
    /// <param name="templates">模板集。</param>
    /// <param name="name">模板名。</param>
    /// <param name="model">行数据模型。</param>
    /// <param name="fallback">模板缺失时的回退节点（null ⇒ 空）。</param>
    public static IReadOnlyList<UiNode> RenderToNodes(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?>? model,
        IReadOnlyList<UiNode>? fallback = null)
    {
        if (templates is null || !templates.TryGetValue(name, out var tpl) || string.IsNullOrWhiteSpace(tpl))
            return fallback ?? Array.Empty<UiNode>();
        return RenderTemplateToNodes(tpl, templates, model);
    }

    /// <summary>渲染模板字符串为节点树（唯一入口：<see cref="RenderToNodes"/> 的下层）。</summary>
    /// <param name="template">模板原文。</param>
    /// <param name="templates">模板集（供 partial 解析，可 null）。</param>
    /// <param name="model">行数据模型。</param>
    public static IReadOnlyList<UiNode> RenderTemplateToNodes(
        string template,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?>? model)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<UiNode>();

        // 节点出口 = **原生节点**（不经「渲染成串再解析」）：|raw 位由 UiMarkup 直接携带，
        // 形态转换点唯一（模板 → 节点），不再产生 HTML→节点回解析（E6）。
        var output = new List<UiNode>();
        RenderNodesTo(ParseWhole(template), templates, model ?? EmptyModel, output);
        return output;
    }

    /// <summary>节点渲染（与 <see cref="RenderNodes"/> 同构；输出节点列表而非字符串）。</summary>
    private static void RenderNodesTo(
        List<Node> nodes,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?> model,
        List<UiNode> output)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    if (t.Text.Length > 0) output.Add(UiNode.RawText(t.Text));
                    break;
                case BindingNode b:
                    if (b.Raw)
                    {
                        // 节点出口同受类型屏障约束：|raw 只接受 UiMarkup；UiText 走文本节点（序列化时编码）。
                        var raw = GetValue(model, b.Field);
                        switch (raw)
                        {
                            case null:
                                break;
                            case UiMarkup markup:
                                output.Add(UiNode.Markup(markup));
                                break;
                            case UiText text:
                                if (text.Value.Length > 0) output.Add(UiNode.TextNode(text.Value));
                                break;
                            default:
                                throw RawBindingTypeError(b.Field, raw);
                        }

                        break;
                    }

                    var val = Get(model, b.Field);
                    if (val is not null) output.Add(UiNode.TextNode(val));
                    break;
                case EachNode e:
                    if (TryGetList(model, e.ListName, out var list))
                    {
                        foreach (var item in list)
                            RenderNodesTo(e.Children, templates, ToModel(item), output);
                    }
                    break;
                case CondNode c:
                    RenderNodesTo(EvalCond(model, c.Cond) ? c.If : c.Else, templates, model, output);
                    break;
                case NamedNode nn:
                    // `{>@field}`：partial 名由数据给出（声明驱动分派）；`{>name}`：静态 partial。
                    var dynamicName = nn.Name.StartsWith('@') ? Get(model, nn.Name[1..]) : nn.Name;
                    if (dynamicName is not null && templates is not null &&
                        templates.TryGetValue(dynamicName, out var childTpl))
                        RenderNodesTo(ParseWhole(childTpl), templates, model, output);
                    break;
            }
        }
    }

    /// <summary>|raw 位类型错误（唯一构造点）：消息含字段名与修复指引，便于定位 feeder。</summary>
    private static InvalidOperationException RawBindingTypeError(string field, object value)
        => new($"模板绑定 '{{{field}|raw}}' 只接受 UiMarkup（受信片段）或 UiText；收到 {value.GetType().FullName}。"
               + "修复：文本经 UiMarkup.FromText（需编码）或 UiMarkup.Trusted（已编码受信）后传入。");

    /// <summary>整段模板解析（结果缓存，避免反复解析同一模板）。</summary>
    private static List<Node> ParseWhole(string template)
        => ParseCache.GetOrAdd(template, static t =>
        {
            int i = 0;
            return ParseNodes(t, ref i, out _);
        });

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
                    var value = GetValue(model, b.Field);
                    if (value is null) break;
                    if (b.Raw)
                    {
                        // 类型屏障：|raw 位只接受受信片段（UiMarkup）/待编码文本（UiText）。
                        // 裸 string 一律 fail-fast —— 否则任意字符串可当受信 HTML 绕过组合层屏障（E5）。
                        sb.Append(value switch
                        {
                            UiMarkup markup => markup.Html,
                            UiText text => WebUtility.HtmlEncode(text.Value),
                            _ => throw RawBindingTypeError(b.Field, value),
                        });
                    }
                    else
                    {
                        sb.Append(WebUtility.HtmlEncode(value is UiText t ? t.Value : Get(model, b.Field) ?? string.Empty));
                    }

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
                    // `{>@field}`：partial 名由数据给出（声明驱动分派）；`{>name}`：静态 partial。
                    var dynamicName = nn.Name.StartsWith('@') ? Get(model, nn.Name[1..]) : nn.Name;
                    if (dynamicName is not null && templates is not null &&
                        templates.TryGetValue(dynamicName, out var childTpl))
                        RenderNodes(ParseWhole(childTpl), templates, model, sb);
                    break;
            }
        }
    }

    /// <summary>条件求值：<c>{?field=value}</c> 等式比较（大小写不敏感）；否则按统一真值语义
    /// （见 <see cref="UiModel.Truthy"/>：<c>"0"/"false"/空白</c> 为假）。</summary>
    private static bool EvalCond(IReadOnlyDictionary<string, object?> model, string cond)
    {
        // 支持 field!=value / field==value / field=value；否则按统一真值语义。
        int neq = cond.IndexOf("!=", StringComparison.Ordinal);
        if (neq > 0)
            return !string.Equals(UiModel.Get(model, cond[..neq].Trim()), cond[(neq + 2)..].Trim(), StringComparison.OrdinalIgnoreCase);
        int eq = cond.IndexOf('=');
        if (eq > 0)
        {
            var field = cond[..eq].Trim();
            var rest = cond[(eq + 1)..];
            if (rest.StartsWith("=", StringComparison.Ordinal)) rest = rest[1..];
            return string.Equals(UiModel.Get(model, field), rest.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        return UiModel.Truthy(UiModel.Get(model, cond));
    }

    private static IReadOnlyDictionary<string, object?> ToModel(object? item) => UiModel.ToModel(item);

    private static bool TryGetList(
        IReadOnlyDictionary<string, object?> model,
        string name,
        out IEnumerable<object?> list)
        => UiModel.TryGetList(model, name, out list);

    /// <summary>取值（大小写不敏感，bool 归一为 <c>"1"/""</c>），缺失 ⇒ null。</summary>
    private static string? Get(IReadOnlyDictionary<string, object?> model, string field)
        => UiModel.Get(model, field);

    /// <summary>取原始值对象（类型化绑定：识别 <see cref="UiMarkup"/> / <see cref="UiText"/>）。</summary>
    private static object? GetValue(IReadOnlyDictionary<string, object?> model, string field)
    {
        if (model.TryGetValue(field, out var direct)) return direct;
        foreach (var pair in model)
        {
            if (string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }

        return null;
    }
}
