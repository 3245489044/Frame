#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Presentation;

/// <summary>
/// 渲染区域标识（**结构词汇唯一来源**）：区域是框架概念（轮折叠/前导/段首/行/附属行/操作面板/降级/空态），
/// 其**模板键取值**由声明面 <c>ui/presentation.regions.json</c> 给出，本类不含任何具体模板名。
/// </summary>
public static class TemplateRegions
{
    /// <summary>轮段折叠区域。</summary>
    public const string RoundFold = "roundFold";

    /// <summary>段首前导折叠区域。</summary>
    public const string LeadFold = "leadFold";

    /// <summary>轮段（无折叠）区域。</summary>
    public const string SectionHeader = "sectionHeader";

    /// <summary>消息行区域（按类分派到具体行模板）。</summary>
    public const string Row = "row";

    /// <summary>附属行区域。</summary>
    public const string AttachItem = "attachItem";

    /// <summary>操作面板区域。</summary>
    public const string ActionPanel = "actionPanel";

    /// <summary>降级提示区域。</summary>
    public const string RenderDegraded = "renderDegraded";

    /// <summary>空态区域。</summary>
    public const string EmptyP = "emptyP";

    /// <summary>全部区域标识（完备性判据载体）。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        RoundFold, LeadFold, SectionHeader, Row, AttachItem, ActionPanel, RenderDegraded, EmptyP,
    };
}

/// <summary>声明面渲染面条目（面标识 + 缓存带 + 声明序 + 启用位）。</summary>
/// <param name="Id">面标识（开集受控词：headPrompt/operation/dialogue）。</param>
/// <param name="Band">缓存带（编排序第一判据）。</param>
/// <param name="Order">同带内声明序。</param>
/// <param name="Enabled">是否启用该面。</param>
public sealed record DeclaredSurface(string Id, int Band, int Order, bool Enabled);

/// <summary>
/// 声明面视角（Lens）条目：一个视角 = 一组声明式过滤谓词（呈现类 / 结构面）+ 报数所需标识。
/// <para>谓词为空 ⇒ 不过滤（全量视角）；过滤结果必须报数（<c>Selected + FilteredOut == Total</c>）。</para>
/// </summary>
/// <param name="Id">视角标识（受控词，声明面给出）。</param>
/// <param name="LabelKey">文案键（labels.json 键；缺省 = 标识自身）。</param>
/// <param name="Classes">命中即选中的呈现类（空 = 不限类）。</param>
/// <param name="Surfaces">命中即选中的结构面词（bubble/process/operation/notice；空 = 不限面）。</param>
/// <param name="IsDefault">是否为默认视角。</param>
public sealed record DeclaredLens(
    string Id,
    string LabelKey,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> Surfaces,
    bool IsDefault = false)
{
    /// <summary>命中即选中的分类标识（空 = 不限分类）。</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>谓词是否为空（⇒ 全量视角）。</summary>
    public bool IsUnfiltered => Classes.Count == 0 && Surfaces.Count == 0 && Categories.Count == 0;
}

/// <summary>
/// **渲染区域/模板键声明**（产品策略单源：<c>ui/presentation.regions.json</c>）。
/// <para>
/// 框架只认识区域标识（<see cref="TemplateRegions"/>），区域对应的模板键、唯一兜底模板、
/// 必需模板集、渲染面顺序全部由本声明给出；声明缺失 ⇒ <see cref="Empty"/>
/// ⇒ 渲染链可见降级（禁在框架内保留模板名字面量作默认值）。
/// </para>
/// </summary>
public sealed record PresentationRegions(
    IReadOnlyDictionary<string, string> RegionKeys,
    string DefaultTemplate,
    IReadOnlyList<string> RequiredTemplates,
    IReadOnlyList<DeclaredSurface> Surfaces)
{
    /// <summary>空声明（声明不可用）：无区域键、无兜底模板。</summary>
    public static readonly PresentationRegions Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        string.Empty,
        Array.Empty<string>(),
        Array.Empty<DeclaredSurface>());

    /// <summary>声明是否可用（区域键与兜底模板齐备）。</summary>
    public bool IsDeclared => RegionKeys.Count > 0 && DefaultTemplate.Length > 0;

    /// <summary>声明面视角集（空 = 未声明 ⇒ 视角能力不可用，调用方须显式可见化）。</summary>
    public IReadOnlyList<DeclaredLens> Lenses { get; init; } = Array.Empty<DeclaredLens>();

    /// <summary>
    /// 生效必需模板键（**单一来源投影**）：<see cref="RequiredTemplates"/> ∪ 区域键 ∪ 兜底模板。
    /// <para>用途：模板集完备性校验只读本属性，禁再维护第二份键清单（区域键漏配 ⇒ 可见缺失）。</para>
    /// </summary>
    public IReadOnlyList<string> RequiredKeys
    {
        get
        {
            var keys = new List<string>(RequiredTemplates.Count + RegionKeys.Count + 1);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string? key)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                if (seen.Add(key!)) keys.Add(key!);
            }

            Add(DefaultTemplate);
            foreach (var key in RequiredTemplates) Add(key);
            foreach (var key in RegionKeys.Values) Add(key);
            return keys;
        }
    }

    /// <summary>区域 → 模板键（未声明 ⇒ 空串；调用方据此可见降级）。</summary>
    /// <param name="region">区域标识（<see cref="TemplateRegions"/>）。</param>
    public string KeyOf(string region)
        => RegionKeys.TryGetValue(region, out var key) ? key : string.Empty;

    /// <summary>
    /// 解析声明 JSON。形制：
    /// <c>{ "regions": { "&lt;区域&gt;": "&lt;模板键&gt;" }, "defaults": { "template": "…" },
    /// "templates": { "required": [ … ] }, "surfaces": [ { "id": "…", "band": 0, "order": 0, "enabled": true } ] }</c>
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    public static PresentationRegions Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = ConversationJson.Root(json);

        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("regions", out var regionMap) && regionMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in regionMap.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                var key = entry.Value.GetString();
                if (!string.IsNullOrWhiteSpace(key)) regions[entry.Name] = key!.Trim();
            }
        }

        var defaultTemplate = string.Empty;
        if (root.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object
            && defaults.TryGetProperty("template", out var template) && template.ValueKind == JsonValueKind.String)
        {
            defaultTemplate = (template.GetString() ?? string.Empty).Trim();
        }

        var required = new List<string>();
        if (root.TryGetProperty("templates", out var templates) && templates.ValueKind == JsonValueKind.Object
            && templates.TryGetProperty("required", out var requiredElement)
            && requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var key = item.GetString();
                if (!string.IsNullOrWhiteSpace(key)) required.Add(key!.Trim());
            }
        }

        var surfaces = new List<DeclaredSurface>();
        if (root.TryGetProperty("surfaces", out var surfaceList) && surfaceList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in surfaceList.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = ReadString(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                surfaces.Add(new DeclaredSurface(
                    id!.Trim(),
                    ReadInt(item, "band", 0),
                    ReadInt(item, "order", surfaces.Count),
                    !item.TryGetProperty("enabled", out var enabled)
                    || enabled.ValueKind != JsonValueKind.False));
            }
        }

        return new PresentationRegions(regions, defaultTemplate, required, surfaces)
        {
            Lenses = ParseLenses(root),
        };
    }

    /// <summary>解析视角声明段：<c>"lenses": [ { "id": "…", "labelKey": "…", "classes": [ … ], "surfaces": [ … ], "default": true } ]</c>。</summary>
    private static IReadOnlyList<DeclaredLens> ParseLenses(JsonElement root)
    {
        if (!root.TryGetProperty("lenses", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DeclaredLens>();
        }

        var lenses = new List<DeclaredLens>(list.GetArrayLength());
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            lenses.Add(new DeclaredLens(
                id!.Trim(),
                ReadString(item, "labelKey")?.Trim() ?? id.Trim(),
                ReadStringList(item, "classes"),
                ReadStringList(item, "surfaces"),
                item.TryGetProperty("default", out var isDefault) && isDefault.ValueKind == JsonValueKind.True)
            {
                Categories = ReadStringList(item, "categories"),
            });
        }

        return lenses;
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value!.Trim().ToLowerInvariant());
        }

        return values;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.Number
           && property.TryGetInt32(out var value)
            ? value
            : fallback;
}
