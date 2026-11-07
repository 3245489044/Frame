#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Frame;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>合成用声明注入（宿主装配；框架零领域词）。</summary>
/// <param name="Presentation">呈现声明。</param>
/// <param name="Fields">字段词表。</param>
/// <param name="KindFields">kind → 需呈现的字段名（有序）。</param>
public sealed record FrameComposerOptions(
    PresentationDeclaration Presentation,
    FieldDeclaration Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> KindFields)
{
    /// <summary>空声明（一切字段 unknown、无气泡面）。</summary>
    public static readonly FrameComposerOptions Empty = new(
        PresentationDeclaration.Empty,
        FieldDeclaration.Empty,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}

/// <summary>
/// 帧合成器（唯一实现）：渲染项 + 声明 → RenderPlan（字段不压平）。
/// 分组/配对/锚点复用 AttachPlanner；本类只做字段投影。
/// </summary>
public static class FrameComposer
{
    private static readonly string[] FactFields = { "terminal", "archived", "presentation", "origin" };

    /// <summary>合成计划（items 源序；policy 可空 ⇒ 扁平）。</summary>
    public static RenderPlan Compose(
        IReadOnlyList<ConversationRenderItem> items,
        FramePolicy? policy,
        FrameComposerOptions options,
        string viewKey = "")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var effective = policy ?? new FramePolicy(
            new Dictionary<string, FrameRoleSpec>(StringComparer.Ordinal),
            FrameGroupLabels.Default,
            options.Presentation);

        var top = AttachPlanner.Plan(items, policy);
        var roots = new List<UiSurfaceNode>();

        if (top.Lead.Count > 0)
        {
            var leadRows = new List<UiSurfaceNode>(top.Lead.Count);
            for (var i = 0; i < top.Lead.Count; i++)
            {
                leadRows.Add(BuildRow(top.Lead[i], $"lead:{i}", options, null, null));
            }

            roots.Add(BuildGroupNode("lead", "lead", leadRows, options.Presentation));
        }

        var anchorIndex = 0;
        foreach (var entry in top.Entries)
        {
            var resolved = PresentationResolver.Resolve(options.Presentation, entry.Anchor);
            var role = options.Presentation.IsBubbleSurface(resolved.ClassId)
                ? SurfaceRoleIds.Bubble
                : SurfaceRoleIds.FieldRow;

            var children = new List<UiSurfaceNode>();
            foreach (var group in AttachPlanner.PlanGroups(entry.Attach, effective).Groups)
            {
                var rows = new List<UiSurfaceNode>(group.Rows.Count);
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    var row = group.Rows[i];
                    rows.Add(BuildRow(row.Source, $"{group.Kind}:{i}", options, row.ResultText, row.ResultLabel));
                }

                children.Add(BuildGroupNode($"group:{group.Kind}", group.Label, rows, options.Presentation));
            }

            roots.Add(new UiSurfaceNode(
                role,
                $"anchor:{anchorIndex}",
                options.Presentation.SurfaceOf(resolved.ClassId),
                resolved.ClassId,
                BuildFields(entry.Anchor, resolved.Origin, options, null, null),
                children,
                UiNodeState.Open,
                new[] { "toggleFold" }));
            anchorIndex++;
        }

        return new RenderPlan(viewKey, roots);
    }

    private static UiSurfaceNode BuildRow(
        ConversationRenderItem item,
        string key,
        FrameComposerOptions options,
        string? resultText,
        string? resultLabel)
    {
        var resolved = PresentationResolver.Resolve(options.Presentation, item);
        return new UiSurfaceNode(
            SurfaceRoleIds.FieldRow,
            $"row:{key}",
            options.Presentation.SurfaceOf(resolved.ClassId),
            resolved.ClassId,
            BuildFields(item, resolved.Origin, options, resultText, resultLabel),
            Array.Empty<UiSurfaceNode>(),
            UiNodeState.Open,
            new[] { "toggleFold", "copy" });
    }

    private static UiSurfaceNode BuildGroupNode(
        string key,
        string label,
        IReadOnlyList<UiSurfaceNode> children,
        PresentationDeclaration presentation)
        => new(
            SurfaceRoleIds.Group,
            key,
            PresentationSurface.Process,
            presentation.GroupClass is { Length: > 0 } declared ? declared : "process",
            new[] { new UiField("group", SurfaceCellIds.Badge, label) },
            children,
            UiNodeState.Closed,
            new[] { "toggleFold" });

    private static IReadOnlyList<UiField> BuildFields(
        ConversationRenderItem item,
        PresentationOrigin origin,
        FrameComposerOptions options,
        string? resultText,
        string? resultLabel)
    {
        var fields = new List<UiField>();
        var kind = item.Stage ?? string.Empty;
        if (options.KindFields.TryGetValue(kind, out var declared))
        {
            foreach (var name in declared)
            {
                fields.Add(BuildField(item, name, origin, options));
            }
        }

        foreach (var fact in FactFields)
        {
            fields.Add(BuildField(item, fact, origin, options));
        }

        if (resultText is not null)
        {
            fields.Add(new UiField("result", SurfaceCellIds.Code, resultText, resultLabel, new[] { "copy" }));
        }

        return fields;
    }

    private static UiField BuildField(
        ConversationRenderItem item, string name, PresentationOrigin origin, FrameComposerOptions options)
    {
        var spec = options.Fields.Resolve(name);
        var value = ValueOf(item, name, origin);
        if (value is null) return UiField.Unknown(name);

        // 字段不压平：声明为 structured 的单元，其「紧凑 JSON 文本」在此反解析为结构值；
        // 形似 JSON 但解析失败 ⇒ 可见 unknown（禁把坏串当正常值继续传）。
        if (value is string text
            && string.Equals(spec.Shape, FieldDeclaration.StructuredShape, StringComparison.Ordinal)
            && LooksStructured(text))
        {
            return TryStructured(text, out var structured)
                ? new UiField(name, spec.Cell, structured, spec.LabelKey, spec.Caps)
                : UiField.Unknown(name);
        }

        return new UiField(name, spec.Cell, value, spec.LabelKey, spec.Caps);
    }

    private static bool LooksStructured(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    /// <summary>JSON 文本 → 结构值（对象 = 键值表；数组 = 项表；其它 ⇒ 失败）。</summary>
    private static bool TryStructured(string text, out object? value)
    {
        value = null;
        try
        {
            var node = JsonNode.Parse(text);
            switch (node)
            {
                case JsonObject obj:
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in obj) map[pair.Key] = pair.Value?.ToJsonString() ?? string.Empty;
                    value = map;
                    return true;
                case JsonArray array:
                    var list = new List<string>(array.Count);
                    foreach (var element in array) list.Add(element?.ToJsonString() ?? string.Empty);
                    value = list;
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? ValueOf(ConversationRenderItem item, string name, PresentationOrigin origin)
        => name switch
        {
            "text" => string.IsNullOrEmpty(item.Text) ? null : item.Text,
            "terminal" => item.Terminal,
            "archived" => item.Archived,
            "presentation" => item.Presentation,
            "origin" => origin.ToString(),
            _ => item.Fields is not null && item.Fields.TryGetValue(name, out var value) ? value : null,
        };
}
