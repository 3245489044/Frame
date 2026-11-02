#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiBackend.Html.Presentation;

/// <summary>字段可见性（受控词；`hidden` / `placeholder` / `visible`）。</summary>
public enum FieldVisibility
{
    Visible = 0,
    Placeholder = 1,
    Hidden = 2,
}

/// <summary>区域分组策略（受控词；`none` = 不按轮分组、不产轮段头）。</summary>
public enum RegionGrouping
{
    None = 0,
    ByRound = 1,
}

/// <summary>区域呈现规则：该区域允许哪些字段、如何分组、是否抑制段头。</summary>
public sealed record RegionPresentationRule(
    string Region,
    RegionGrouping Grouping = RegionGrouping.None,
    bool SuppressHeader = true,
    IReadOnlySet<string>? Fields = null)
{
    /// <summary>字段是否允许出现在该区域行模型（未声明字段集 ⇒ 允许）。</summary>
    public bool Allows(string field)
        => Fields is null || Fields.Count == 0 || Fields.Contains(field);
}

/// <summary>字段呈现规则：是否可见 + 空值出口。</summary>
public sealed record FieldPresentation(string Field, FieldVisibility Visibility = FieldVisibility.Visible);

/// <summary>
/// 视图呈现契约（**Framework 侧唯一权威**）：视图 → 区域规则 → 字段可见性。
/// <para>领域只提供声明数据，不写 <c>if (field)</c>；渲染层按本契约裁剪行模型。</para>
/// </summary>
public sealed record PresentationContract(
    string ViewKey,
    IReadOnlyDictionary<string, RegionPresentationRule> Regions,
    IReadOnlyDictionary<string, FieldPresentation> Fields)
{
    /// <summary>空契约（未声明视图）：不裁剪、按轮分组（零回归的显式缺省）。</summary>
    public static PresentationContract Empty(string viewKey)
        => new(viewKey,
            new Dictionary<string, RegionPresentationRule>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, FieldPresentation>(StringComparer.OrdinalIgnoreCase));

    /// <summary>区域规则（未声明 ⇒ 不裁剪；分组按轮）。</summary>
    public RegionPresentationRule RegionOf(string? region)
        => region is not null && Regions.TryGetValue(region, out var rule)
            ? rule
            : new RegionPresentationRule(region ?? string.Empty, RegionGrouping.ByRound, SuppressHeader: false);

    /// <summary>字段可见性（未声明 ⇒ 可见）。</summary>
    public FieldVisibility VisibilityOf(string field)
        => Fields.TryGetValue(field, out var rule) ? rule.Visibility : FieldVisibility.Visible;

    /// <summary>字段是否可见。</summary>
    public bool IsVisible(string field) => VisibilityOf(field) != FieldVisibility.Hidden;

    /// <summary>区域行模型裁剪（隐藏字段直接移除，模板判空即不渲染）。</summary>
    public void ApplyToRow(string region, IDictionary<string, object?> row)
    {
        if (row is null || row.Count == 0) return;
        var rule = RegionOf(region);
        foreach (var key in row.Keys.ToArray())
        {
            if (!rule.Allows(key) || !IsVisible(key)) row.Remove(key);
        }
    }
}

/// <summary>呈现契约声明装载（JSON → 契约）。</summary>
public static class PresentationContractParser
{
    /// <summary>解析声明文本；不可解析 ⇒ null（调用方可见降级）。</summary>
    public static PresentationContract? Parse(string? json, string viewKey)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null) return null;

            var fields = new Dictionary<string, FieldPresentation>(StringComparer.OrdinalIgnoreCase);
            if (root["fields"] is JsonObject fieldTable)
            {
                foreach (var pair in fieldTable)
                {
                    var visibility = ParseVisibility(pair.Value?["visible"]?.GetValue<bool?>());
                    fields[pair.Key] = new FieldPresentation(pair.Key, visibility);
                }
            }

            var regions = new Dictionary<string, RegionPresentationRule>(StringComparer.OrdinalIgnoreCase);
            if (root["regions"] is JsonObject regionTable)
            {
                foreach (var pair in regionTable)
                {
                    var node = pair.Value;
                    var grouping = string.Equals(node?["grouping"]?.GetValue<string>(), "by-round", StringComparison.OrdinalIgnoreCase)
                        ? RegionGrouping.ByRound
                        : RegionGrouping.None;
                    var suppress = node?["suppressHeader"]?.GetValue<bool?>() ?? grouping == RegionGrouping.None;
                    var declared = node?["fields"] is JsonArray array
                        ? array.Select(item => item?.GetValue<string>())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : null;
                    regions[pair.Key] = new RegionPresentationRule(pair.Key, grouping, suppress, declared);
                }
            }

            return new PresentationContract(viewKey, regions, fields);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FieldVisibility ParseVisibility(bool? visible)
        => visible switch
        {
            false => FieldVisibility.Hidden,
            null => FieldVisibility.Visible,
            true => FieldVisibility.Visible,
        };
}
