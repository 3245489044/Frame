#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Frame;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 帧字段契约（声明 <c>frame-kinds.json</c> 的 <c>fields[]</c> 单向投影）。
/// <param name="Name">呈现字段名（字段词表的键）。</param>
/// <param name="From">来源词形（原始观测帧字段；<c>message</c> 为 text 的唯一来源）。</param>
/// <param name="ApplicableWhen">适用条件（受控词 <c>always / message-present / field-present</c>）。</param>
/// </summary>
public sealed record FrameFieldContract(string Name, string From, string ApplicableWhen)
{
    /// <summary>恒适用（缺失值以可见 unknown 呈现）。</summary>
    public const string Always = "always";

    /// <summary>仅当消息体存在时适用。</summary>
    public const string MessagePresent = "message-present";

    /// <summary>仅当来源字段存在时适用。</summary>
    public const string FieldPresent = "field-present";

    /// <summary>受控词（门禁与解析共用）。</summary>
    public static readonly IReadOnlyList<string> Words =
        new[] { Always, MessagePresent, FieldPresent };
}

/// <summary>合成用声明注入（宿主装配；框架零领域词）。</summary>
/// <param name="Presentation">呈现声明。</param>
/// <param name="Fields">字段词表。</param>
/// <param name="KindFields">kind → 需呈现的字段名（有序）。</param>
public sealed record FrameComposerOptions(
    PresentationDeclaration Presentation,
    FieldDeclaration Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> KindFields)
{
    /// <summary>
    /// kind → 字段契约（from/applicableWhen）。空 ⇒ 回退 <see cref="KindFields"/>（可见降级）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FrameFieldContract>> FieldContracts { get; init; }
        = new Dictionary<string, IReadOnlyList<FrameFieldContract>>(StringComparer.Ordinal);

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
        // 计数单源：行/合并由同一分组实现产出（禁第二套聚合）。
        var rowCount = 0;
        var mergedCount = 0;

        if (top.Lead.Count > 0)
        {
            var leadRows = new List<UiSurfaceNode>(top.Lead.Count);
            for (var i = 0; i < top.Lead.Count; i++)
            {
                leadRows.Add(BuildRow(top.Lead[i], $"lead:{i}", options, null, null));
            }

            rowCount += leadRows.Count;
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
            var attachPlan = AttachPlanner.PlanGroups(entry.Attach, effective);
            foreach (var group in attachPlan.Groups)
            {
                var rows = new List<UiSurfaceNode>(group.Rows.Count);
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    var row = group.Rows[i];
                    rows.Add(BuildRow(row.Source, $"{group.Kind}:{i}", options, row.ResultText, row.ResultLabel));
                }

                children.Add(BuildGroupNode($"group:{group.Kind}", group.Label, rows, options.Presentation));
            }

            // 行数 = 组内行；合并数 = 输入帧 - 行（同一实现给出，禁差值倒推）。
            rowCount += 1 + attachPlan.RowCount;
            mergedCount += entry.Attach.Count - attachPlan.RowCount;

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

        return new RenderPlan(viewKey, roots)
        {
            Counts = new RenderPlanCounts(items.Count, rowCount, mergedCount),
        };
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
        if (options.FieldContracts.TryGetValue(kind, out var contracts))
        {
            foreach (var contract in contracts)
            {
                if (!Applies(contract, item)) continue;
                var field = BuildField(item, contract.Name, contract.From, origin, options);
                if (field.IsNotApplicable) continue;
                fields.Add(field);
            }
        }
        else if (options.KindFields.TryGetValue(kind, out var declared))
        {
            // 未声明字段契约 ⇒ 旧口径（name 即来源），保持既有可见性。
            foreach (var name in declared)
            {
                var field = BuildField(item, name, name, origin, options);
                if (field.IsNotApplicable) continue;
                fields.Add(field);
            }
        }

        if (resultText is not null)
        {
            fields.Add(new UiField("result", SurfaceCellIds.Code, resultText, resultLabel, new[] { "copy" }));
        }

        return fields;
    }

    /// <summary>契约适用条件判定（不满足 ⇒ NotApplicable，不渲染）。</summary>
    private static bool Applies(FrameFieldContract contract, ConversationRenderItem item)
        => contract.ApplicableWhen switch
        {
            FrameFieldContract.MessagePresent =>
                !string.IsNullOrEmpty(item.Text) || FieldOf(item, "message") is not null,
            FrameFieldContract.FieldPresent => FieldOf(item, contract.From) is not null,
            _ => true,
        };

    private static UiField BuildField(
        ConversationRenderItem item, string name, string from, PresentationOrigin origin, FrameComposerOptions options)
    {
        var spec = options.Fields.Resolve(name);
        var value = ValueOf(item, from, origin);
        if (value is null)
        {
            return UiField.Unknown(name, spec.LabelKey, "missing-value", name);
        }

        // 字段不压平：声明为 structured 的单元，其「紧凑 JSON 文本」在此反解析为结构值；
        // 形似 JSON 但解析失败 ⇒ 可见 unknown（禁把坏串当正常值继续传）。
        if (value is string text
            && string.Equals(spec.Shape, FieldDeclaration.StructuredShape, StringComparison.Ordinal)
            && LooksStructured(text))
        {
            return TryStructured(text, out var structured)
                ? new UiField(name, spec.Cell, structured, spec.LabelKey, spec.Caps)
                : UiField.Unknown(name, spec.LabelKey, "structured-parse-failed", name);
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
            "text" => !string.IsNullOrEmpty(item.Text)
                ? item.Text
                : FieldOf(item, "message"),
            "message" => !string.IsNullOrEmpty(item.Text) ? item.Text : FieldOf(item, "message"),
            "terminal" => item.Terminal,
            "archived" => item.Archived,
            "presentation" => item.Presentation,
            "origin" => origin.ToString(),
            _ => FieldOf(item, name),
        };

    private static object? FieldOf(ConversationRenderItem item, string name)
    {
        if (item.Fields is null || !item.Fields.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value;
    }
}
