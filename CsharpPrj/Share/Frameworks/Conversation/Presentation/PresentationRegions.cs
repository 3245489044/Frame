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
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

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

        return new PresentationRegions(regions, defaultTemplate, required, surfaces);
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
