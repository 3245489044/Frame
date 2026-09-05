#nullable enable

using System.Text.Json;
using Plate.JsonMethod.Utilities;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 声明解析器（跨宿主通用）——统一反序列化 + 控件索引 + 内部页面声明提取 + 占位注入。
/// [搬运自 Agent.Launcher 侧的 HtmlBackendRuntime.BuildControlIndex 前身 / ConversationPageRenderer.LoadPageLayout /
/// AgentUiLibraryHtml 的零散解析下沉，统一 options]
/// </summary>
public static class HtmlDeclarationParser
{
    /// <summary>声明反序列化统一 options（PropertyNameCaseInsensitive=true——JSON 小写属性 ↔ CLR PascalCase 的唯一绑定点；
    /// 注册 <see cref="StyleConfigConverter"/> 支持 style 双格式——命名样式字符串/内联对象，修复 style 字符串反序列化抛 JsonException）。</summary>
    public static readonly JsonSerializerOptions JsonOpts = CreateJsonOpts();

    private static JsonSerializerOptions CreateJsonOpts()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opts.Converters.Add(new StyleConfigConverter());
        return opts;
    }

    /// <summary>JSON → UIConfiguration（统一 options；null/空 → null）。</summary>
    public static UIConfiguration? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        return JsonSerializer.Deserialize<UIConfiguration>(json, JsonOpts);
    }

    /// <summary>域名常量（P4 域隔离）：外层宿主物理控件 <c>host:</c>；htmlPage 内元素 <c>page:</c>。
    /// 同名控件按域前缀消歧，output/脚本 target 显式带域。设计理由：外层承载 events.message 的事件桥定位与写回，
    /// 页内元素承载渲染内容，两者同裸名时语义含混（见 8.26 重构方案 R4）。</summary>
    public const string HostDomainPrefix = "host:";
    public const string PageDomainPrefix = "page:";

    /// <summary>把控件名提升到 host 域（外层物理控件定位键）。</summary>
    public static string HostKey(string name) => HostDomainPrefix + name;

    /// <summary>把控件名提升到 page 域（htmlPage 内元素定位键）。</summary>
    public static string PageKey(string name) => PageDomainPrefix + name;

    /// <summary>控件树 → name 索引（递归全部 view，大小写不敏感）。[搬运自 HtmlBackendRuntime.BuildControlIndex 前身]</summary>
    /// 仅索引物理控件（view 顶层 + 面板嵌套），不含 htmlPage 内部元素（页内元素经 <see cref="BuildDomainControlIndex"/> 的 page 域索引）。</summary>
    public static IReadOnlyDictionary<string, ControlConfig> BuildControlIndex(UIConfiguration config)
    {
        var dict = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase);
        if (config.Views is not null)
        {
            foreach (var view in config.Views)
                IndexControls(view.Controls, dict);
        }
        return dict;
    }

    /// <summary>按域索引（P4 域隔离）：物理控件树 → <c>host:</c> 前缀；htmlPage 内部控件树 → <c>page:</c> 前缀。
    /// 同名裸控件分属两域互不覆盖，output/脚本 target 以 <see cref="HostKey"/> / <see cref="PageKey"/> 显式定位。</summary>
    public static IReadOnlyDictionary<string, ControlConfig> BuildDomainControlIndex(UIConfiguration config)
    {
        var dict = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase);
        if (config.Views is not null)
        {
            foreach (var view in config.Views)
                IndexControlsDomain(view.Controls, dict, HostDomainPrefix);
        }
        return dict;
    }

    private static void IndexControls(List<ControlConfig>? controls, Dictionary<string, ControlConfig> dict)
    {
        if (controls is null) return;
        foreach (var c in controls)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                dict[c.Name] = c;
            IndexControls(c.Controls, dict);
        }
    }

    private static void IndexControlsDomain(List<ControlConfig>? controls, Dictionary<string, ControlConfig> dict, string domain)
    {
        if (controls is null) return;
        foreach (var c in controls)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                dict[domain + c.Name] = c;
            // htmlPage 内部控件树归属 page 域（与物理控件 host 域隔离）
            if (c.HtmlPage?.Controls is { Count: > 0 } pageControls)
                IndexControlsDomain(pageControls, dict, PageDomainPrefix);
            IndexControlsDomain(c.Controls, dict, domain);
        }
    }

    /// <summary>取 htmlviewer 控件内嵌的内部页面声明（ControlConfig.HtmlPage）；无 → null。
    /// <paramref name="pageName"/> 非空时须与声明 Name 匹配（大小写不敏感）。</summary>
    public static UIConfiguration? ExtractHtmlPage(ControlConfig host, string pageName)
    {
        if (host is null) return null;
        var page = host.HtmlPage;
        if (page?.Controls is not { Count: > 0 }) return null;
        if (!string.IsNullOrWhiteSpace(pageName)
            && !string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase))
            return null;

        return new UIConfiguration
        {
            Form = new FormConfig
            {
                Width = page.Width > 0 ? page.Width : 1200,
                Height = page.Height > 0 ? page.Height : 800,
            },
            Views = new List<ViewConfig>
            {
                new() { Name = page.Name ?? "internalPage", Type = "Panel", Controls = page.Controls },
            },
        };
    }

    /// <summary>按宿主控件名定位并提取其内嵌内部页面声明（等价 BuildControlIndex 命中 + ExtractHtmlPage 的组合）；未命中/无内部声明 → null。</summary>
    public static UIConfiguration? FindHtmlPage(UIConfiguration config, string hostControlName, string pageName)
    {
        if (config is null || string.IsNullOrWhiteSpace(hostControlName)) return null;
        if (!BuildControlIndex(config).TryGetValue(hostControlName, out var hostControl))
            return null;
        return ExtractHtmlPage(hostControl, pageName);
    }

    /// <summary>从文件加载并解析整体声明（文件不存在 → null）；仅解析声明，不注入模板。</summary>
    public static UIConfiguration? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return null;
        return Parse(System.IO.File.ReadAllText(path));
    }

    /// <summary>占位注入：把 {name} → 数据片段替换进投影产物（布局壳由声明决定，C# 只供数据）。
    /// [引用 JsonMethod.TemplateFormatter 的「{param} 原文替换」内核，缺失占位保留；未知数据键不替换]</summary>
    public static string InjectData(string bodyHtml, IReadOnlyDictionary<string, string>? data)
    {
        if (string.IsNullOrEmpty(bodyHtml) || data is null || data.Count == 0)
            return bodyHtml;
        var dict = new Dictionary<string, object?>(data.Count);
        foreach (var kv in data)
            dict[kv.Key] = kv.Value;
        return new TemplateFormatter().Format(bodyHtml, dict);
    }
}
