#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// kind → 字段口径（声明键集；<see cref="FrameFieldSchema.Empty"/> ⇒ 旧口径）。
    /// </summary>
    public FrameFieldSchema Schema { get; init; } = FrameFieldSchema.Empty;

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
    /// <summary>折叠种类：段横幅（单行摘要，收起不渲染段体）。</summary>
    public const string BannerFoldKind = "banner";

    /// <summary>折叠种类：行摘要（首个字段为主，展开见其余字段）。</summary>
    public const string RowFoldKind = "row";

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

        // 锚点判定与合成必须用**同一份**生效策略：曾用原始 policy（可空）做 Plan，
        // 却用 effective 做字段合成 ⇒ policy 为空时全部落入 lead，终答恒为 fieldRow（气泡永不成立）。
        var top = AttachPlanner.Plan(items, effective);
        var roots = new List<UiSurfaceNode>();
        var diagnostics = new List<UnknownFieldRecord>();
        // 计数单源：行/合并由同一分组实现产出（禁第二套聚合）。
        var rowCount = 0;
        var mergedCount = 0;

        if (top.Lead.Count > 0)
        {
            var leadPlan = AttachPlanner.PlanLead(top.Lead, effective);
            var segmented = leadPlan.Groups.Count > 1
                           || (leadPlan.Groups.Count == 1
                               && !string.Equals(leadPlan.Groups[0].Kind, "lead", StringComparison.Ordinal));
            foreach (var group in leadPlan.Groups)
            {
                var spec = CategorySpecOf(group.Kind, options.Presentation);
                var leadRows = new List<UiSurfaceNode>(group.Rows.Count);
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    var row = group.Rows[i];
                    leadRows.Add(BuildRow(
                        row.Source, $"lead:{group.Kind}:{i}", options, row.ResultText, row.ResultLabel, diagnostics, spec));
                }

                rowCount += leadRows.Count;
                if (spec is not null
                    && string.Equals(spec.Render, PresentationCategorySpec.RenderStream, StringComparison.Ordinal))
                {
                    // 流式段（思考）：不建组节点，行直接挂父级（保持源序）。
                    roots.AddRange(leadRows);
                    continue;
                }

                var key = segmented ? $"lead:{group.Kind}" : "lead";
                roots.Add(BuildGroupNode(key, group.Label, group.Kind, leadRows, group.Rows, options.Presentation));
            }

            mergedCount += leadPlan.MergedCount;
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
                var spec = CategorySpecOf(group.Kind, options.Presentation);
                var rows = new List<UiSurfaceNode>(group.Rows.Count);
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    var row = group.Rows[i];
                    rows.Add(BuildRow(
                        row.Source, $"{group.Kind}:{i}", options, row.ResultText, row.ResultLabel, diagnostics, spec));
                }

                if (spec is not null
                    && string.Equals(spec.Render, PresentationCategorySpec.RenderStream, StringComparison.Ordinal))
                {
                    // 流式段：行直接挂父级（不建组节点，源序不变）。
                    children.AddRange(rows);
                    continue;
                }

                children.Add(BuildGroupNode(
                    $"group:{group.Kind}", group.Label, group.Kind, rows, group.Rows, options.Presentation));
            }

            // 行数 = 组内行；合并数 = 输入帧 - 行（同一实现给出，禁差值倒推）。
            rowCount += 1 + attachPlan.RowCount;
            mergedCount += entry.Attach.Count - attachPlan.RowCount;

            roots.Add(new UiSurfaceNode(
                role,
                $"anchor:{anchorIndex}",
                options.Presentation.SurfaceOf(resolved.ClassId),
                resolved.ClassId,
                BuildFields(entry.Anchor, resolved.Origin, options, null, null, diagnostics),
                children,
                UiNodeState.Open,
                new[] { "toggleFold" }));
            anchorIndex++;
        }

        return new RenderPlan(viewKey, roots)
        {
            Counts = new RenderPlanCounts(items.Count, rowCount, mergedCount)
            {
                ByCategory = CountCategories(items, options.Presentation),
            },
            Diagnostics = new RenderDiagnostics(diagnostics, Array.Empty<string>()),
        };
    }

    private static UiSurfaceNode BuildRow(
        ConversationRenderItem item,
        string key,
        FrameComposerOptions options,
        string? resultText,
        string? resultLabel,
        List<UnknownFieldRecord> diagnostics,
        PresentationCategorySpec? category)
    {
        var resolved = PresentationResolver.Resolve(options.Presentation, item);
        var rowFold = category?.RowFold == true;
        var fields = BuildFields(item, resolved.Origin, options, resultText, resultLabel, diagnostics);
        if (rowFold)
        {
            fields = Prioritize(fields, options.Presentation.PrimaryOf(resolved.ClassId));
        }

        // 角色只表达载体形态：气泡/折叠行/普通行。结构面仍由声明面裁决（锚点判定不变）。
        var role = rowFold
            ? SurfaceRoleIds.Fold
            : options.Presentation.IsBubbleRender(resolved.ClassId)
                ? SurfaceRoleIds.Bubble
                : SurfaceRoleIds.FieldRow;

        return new UiSurfaceNode(
            role,
            $"row:{key}",
            options.Presentation.SurfaceOf(resolved.ClassId),
            resolved.ClassId,
            fields,
            Array.Empty<UiSurfaceNode>(),
            rowFold ? UiNodeState.Closed : UiNodeState.Open,
            new[] { "toggleFold", "copy" })
        {
            FoldKind = rowFold ? RowFoldKind : null,
            SummaryMaxChars = rowFold ? options.Presentation.RowFoldMaxChars : 0,
            Style = options.Presentation.StyleOf(resolved.ClassId),
        };
    }

    private static UiSurfaceNode BuildGroupNode(
        string key,
        string label,
        string groupKind,
        IReadOnlyList<UiSurfaceNode> children,
        IReadOnlyList<FrameRowSpec<ConversationRenderItem>> rows,
        PresentationDeclaration presentation)
    {
        var spec = CategorySpecOf(groupKind, presentation);
        var abnormal = CountAbnormal(rows);
        var span = SpanOf(rows);

        // 折叠态：open ⇒ 展开；open-on-error ⇒ 有异常才展开；其余收起（旧行为不变）。
        var open = spec is not null && (spec.OpensByDefault || (spec.OpensOnError && abnormal > 0));

        // 段横幅：组头收敛为**单个** header 字段（count/span/abnormal 进结构事实，不再各占一行）。
        var banner = spec is not null
                     && string.Equals(spec.Render, PresentationCategorySpec.RenderBanner, StringComparison.Ordinal);
        var fields = banner
            ? new List<UiField>
            {
                new("header", SurfaceCellIds.Badge, HeaderText(label, rows.Count, span, abnormal)),
            }
            : LegacyFields(label, rows.Count, span, abnormal);

        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = rows.Count.ToString(CultureInfo.InvariantCulture),
        };
        if (span is not null) facts["span"] = span;
        if (abnormal > 0) facts["abnormal"] = abnormal.ToString(CultureInfo.InvariantCulture);

        return new UiSurfaceNode(
            SurfaceRoleIds.Group,
            key,
            PresentationSurface.Process,
            presentation.GroupClass is { Length: > 0 } declaredGroup ? declaredGroup : "process",
            fields,
            children,
            open ? UiNodeState.Open : UiNodeState.Closed,
            new[] { "toggleFold" })
        {
            FoldHint = open ? "open" : "closed",
            FoldKind = banner ? BannerFoldKind : null,
            Facts = facts,
        };
    }

    /// <summary>组头单行文本：<c>label count · span !abnormal</c>（缺省项不渲染）。</summary>
    private static string HeaderText(string label, int count, string? span, int abnormal)
    {
        var text = label + " " + count.ToString(CultureInfo.InvariantCulture);
        if (span is not null) text += " · " + span;
        if (abnormal > 0) text += " !" + abnormal.ToString(CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>旧口径组字段（非分类段/非 banner 段保持原样，零回归）。</summary>
    private static List<UiField> LegacyFields(string label, int count, string? span, int abnormal)
    {
        var fields = new List<UiField>
        {
            new("group", SurfaceCellIds.Badge, label),
            new("count", SurfaceCellIds.Badge, count.ToString(CultureInfo.InvariantCulture), "count"),
        };
        if (span is not null) fields.Add(new UiField("span", SurfaceCellIds.Duration, span, "span"));
        if (abnormal > 0)
        {
            fields.Add(new UiField(
                "abnormal", SurfaceCellIds.Badge, abnormal.ToString(CultureInfo.InvariantCulture), "abnormal"));
        }

        return fields;
    }

    /// <summary>把主字段移到首位（缺失/已是首位/声明 first-field ⇒ 保持原序）。</summary>
    private static IReadOnlyList<UiField> Prioritize(IReadOnlyList<UiField> fields, string primary)
    {
        if (fields.Count < 2
            || string.Equals(primary, PresentationClassSpec.PrimaryFirstField, StringComparison.Ordinal))
        {
            return fields;
        }

        var index = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, primary, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index <= 0) return fields;

        var ordered = new List<UiField>(fields.Count) { fields[index] };
        for (var i = 0; i < fields.Count; i++)
        {
            if (i != index) ordered.Add(fields[i]);
        }

        return ordered;
    }

    /// <summary>分类声明（非分类段 ⇒ null）。</summary>
    private static PresentationCategorySpec? CategorySpecOf(
        string groupKind, PresentationDeclaration presentation)
    {
        var categoryId = CategoryOfKind(groupKind);
        return categoryId is not null && presentation.Categories.TryGetValue(categoryId, out var spec)
            ? spec
            : null;
    }

    /// <summary>分组段词 → 分类标识（<c>category:&lt;id&gt;</c>；非分类段 ⇒ null）。</summary>
    private static string? CategoryOfKind(string kind)
        => kind.StartsWith("category:", StringComparison.Ordinal)
            ? kind.Substring("category:".Length)
            : null;

    /// <summary>组内异常帧数（结构事实判据：帧字段 status/ok；禁文本叙述推断）。</summary>
    private static int CountAbnormal(IReadOnlyList<FrameRowSpec<ConversationRenderItem>> rows)
    {
        var count = 0;
        foreach (var row in rows)
        {
            if (IsAbnormal(row.Source)) count++;
        }

        return count;
    }

    private static bool IsAbnormal(ConversationRenderItem item)
    {
        if (item.Fields is null) return false;
        if (item.Fields.TryGetValue("status", out var status) && !string.IsNullOrEmpty(status))
        {
            return status.Contains("error", StringComparison.OrdinalIgnoreCase)
                   || status.Contains("fail", StringComparison.OrdinalIgnoreCase);
        }

        return item.Fields.TryGetValue("ok", out var ok)
               && string.Equals(ok, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>组时间跨度（秒；有效 at 不足两个 ⇒ null，不渲染）。</summary>
    private static string? SpanOf(IReadOnlyList<FrameRowSpec<ConversationRenderItem>> rows)
    {
        long? min = null;
        long? max = null;
        foreach (var row in rows)
        {
            if (row.Source.Fields is null
                || !row.Source.Fields.TryGetValue("at_unix_ms", out var text)
                || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at))
            {
                continue;
            }

            if (min is null || at < min.Value) min = at;
            if (max is null || at > max.Value) max = at;
        }

        if (min is null || max is null || max.Value <= min.Value) return null;
        var span = TimeSpan.FromMilliseconds(max.Value - min.Value);
        return span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>分类计数（与段分组同一判据；未注册分类归 unknown，可见不静默）。</summary>
    private static IReadOnlyDictionary<string, int> CountCategories(
        IReadOnlyList<ConversationRenderItem> items, PresentationDeclaration presentation)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var classId = PresentationResolver.Resolve(
                presentation, item.Role, item.Channel, item.Presentation, item.Terminal).ClassId;
            var category = presentation.CategoryOf(classId) ?? "unknown";
            counts[category] = counts.TryGetValue(category, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static IReadOnlyList<UiField> BuildFields(
        ConversationRenderItem item,
        PresentationOrigin origin,
        FrameComposerOptions options,
        string? resultText,
        string? resultLabel,
        List<UnknownFieldRecord> diagnostics)
    {
        var fields = new List<UiField>();
        var kind = item.EffectiveKind;
        if (options.FieldContracts.TryGetValue(kind, out var contracts))
        {
            foreach (var contract in contracts)
            {
                if (!Applies(contract, item)) continue;
                var field = BuildField(item, kind, contract, origin, options);
                if (field.IsNotApplicable) continue;
                RecordUnknown(diagnostics, kind, field);
                fields.Add(field);
            }
        }
        else if (options.KindFields.TryGetValue(kind, out var declared))
        {
            // 未声明字段契约 ⇒ 旧口径（name 即来源），保持既有可见性。
            foreach (var name in declared)
            {
                var field = BuildField(
                    item, kind, new FrameFieldContract(name, name, FrameFieldContract.Always), origin, options);
                if (field.IsNotApplicable) continue;
                RecordUnknown(diagnostics, kind, field);
                fields.Add(field);
            }
        }

        if (resultText is not null)
        {
            fields.Add(new UiField("result", SurfaceCellIds.Code, resultText, resultLabel, new[] { "copy" }));
        }

        return fields;
    }

    /// <summary>未知字段记账（J4：每个未知必带 kind/key/reason 三元组）。</summary>
    private static void RecordUnknown(
        List<UnknownFieldRecord> diagnostics, string kind, UiField field)
    {
        if (!field.IsUnknown) return;
        diagnostics.Add(new UnknownFieldRecord(kind, field.Name, field.UnknownReason ?? SurfaceReasonCodes.MissingValue));
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
        ConversationRenderItem item, string kind, FrameFieldContract contract,
        PresentationOrigin origin, FrameComposerOptions options)
    {
        var name = contract.Name;
        var from = contract.From;
        var spec = options.Fields.Resolve(name);
        var value = ValueOf(item, from, origin);
        var required = string.Equals(contract.ApplicableWhen, FrameFieldContract.Always, StringComparison.Ordinal);

        // 字段口径裁决（D 层唯一判据）：消费方禁再按「值是否存在」自行推断三态。
        switch (options.Schema.VerdictOf(kind, from, value is not null, required))
        {
            case FrameFieldVisibility.NotApplicable:
                return UiField.NotApplicable(name, spec.LabelKey, SurfaceReasonCodes.KindHasNoSuchKey);
            case FrameFieldVisibility.Unknown:
                return UiField.Unknown(name, spec.LabelKey, SurfaceReasonCodes.MissingValue, name);
        }

        if (value is null)
        {
            return UiField.Unknown(name, spec.LabelKey, SurfaceReasonCodes.MissingValue, name);
        }

        // 字段不压平：声明为 structured 的单元，其「紧凑 JSON 文本」在此反解析为结构值；
        // 形似 JSON 但解析失败 ⇒ 可见 unknown（禁把坏串当正常值继续传）。
        if (value is string text
            && string.Equals(spec.Shape, FieldDeclaration.StructuredShape, StringComparison.Ordinal)
            && LooksStructured(text))
        {
            return TryStructured(text, out var structured)
                ? new UiField(name, spec.Cell, structured, spec.LabelKey, spec.Caps)
                : UiField.Unknown(name, spec.LabelKey, SurfaceReasonCodes.StructuredParseFailed, name);
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
            // 正文来源单源 = 帧字段袋（message 优先、text 兼容），**不回退 shaping summary**：
            // item.Text 已是塑形摘要（可能等于 stage 等其他字段），回退会造成同一行 `X · X` 重复。
            "text" => FieldOf(item, "text") ?? (string.IsNullOrWhiteSpace(item.Text) ? null : item.Text),
            "message" => FieldOf(item, "message") ?? FieldOf(item, "text"),
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
