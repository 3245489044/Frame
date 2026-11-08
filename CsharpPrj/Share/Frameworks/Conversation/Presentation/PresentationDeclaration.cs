#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Share.Conversation.Presentation;

/// <summary>呈现**结构面**（框架级通用概念：气泡面 / 过程面 / 操作面 / 通知面 / 未声明）。</summary>
public enum PresentationSurface
{
    /// <summary>气泡面（对话行）。</summary>
    Bubble = 0,

    /// <summary>过程面（折叠进下拉栏）。</summary>
    Process = 1,

    /// <summary>操作面（顶部操作面板）。</summary>
    Operation = 2,

    /// <summary>通知面（可见但非对话）。</summary>
    Notice = 3,

    /// <summary>未声明面（呈现类未注册：可见占位，不猜、不成气泡）。</summary>
    Unknown = 4,
}

/// <summary>单个呈现类声明（词 = 产品策略；本类型只承载数据）。</summary>
/// <param name="Id">呈现类标识（声明词，规范化小写）。</param>
/// <param name="Surface">结构面（决定"能不能成气泡/进操作面板"）。</param>
/// <param name="Template">行模板键（渲染层按此键取模板；缺省由宿主给）。</param>
/// <param name="StyleToken">样式 token（缺省 = <paramref name="Id"/>）。</param>
/// <summary>单个呈现类声明（词 = 产品策略；本类型只承载数据）。</summary>
/// <param name="Id">呈现类标识（声明词，规范化小写）。</param>
/// <param name="Surface">结构面（决定"能不能成气泡/进操作面板"）。</param>
/// <param name="Template">行模板键（渲染层按此键取模板）。</param>
/// <param name="StyleToken">样式 token。</param>
public sealed record PresentationClassSpec(
    string Id,
    PresentationSurface Surface,
    string Template,
    string StyleToken,
    bool AnswerBadge = false)
{
    /// <summary>强调 token（视觉策略声明位；缺省 = 不强调）。</summary>
    public string? Emphasis { get; init; }

    /// <summary>行摘要主字段（受控词；缺省 <see cref="PrimaryFirstField"/>）。</summary>
    public string Primary { get; init; } = PrimaryFirstField;

    /// <summary>行形态（受控词；surface 不变，只决定载体角色）。</summary>
    public string Render { get; init; } = RenderRow;

    /// <summary>主字段 = 字段序首个可见字段。</summary>
    public const string PrimaryFirstField = "first-field";

    /// <summary>行形态 = 气泡（结构面仍由 <see cref="Surface"/> 裁决，不作锚点）。</summary>
    public const string RenderBubble = "bubble";

    /// <summary>行形态 = 普通行。</summary>
    public const string RenderRow = "row";

    /// <summary>行形态受控词（声明校验用）。</summary>
    public static readonly IReadOnlyList<string> Renders = new[] { RenderBubble, RenderRow };

    /// <summary>主字段受控词（声明校验用）。</summary>
    public static readonly IReadOnlyList<string> Primaries = new[]
    {
        PrimaryFirstField, "text", "result", "reasoning",
    };
}

/// <summary>
/// **呈现分类声明**（语义分组轴）：一组呈现类 → 一个分类（分区：互斥且覆盖）。
/// <para>分类只决定「显示成哪一段」，不决定呈现身份（身份仍由 <see cref="PresentationResolver"/> 裁决）。</para>
/// </summary>
/// <param name="Id">分类标识（受控词，声明面给出）。</param>
/// <param name="LabelKey">文案键（labels.json；宿主装载时解析为 <see cref="Label"/>）。</param>
/// <param name="Order">段序权重（声明序）。</param>
/// <param name="Fold">默认折叠态（open / closed / open-on-error）。</param>
/// <param name="Classes">归属本分类的呈现类（分区：互斥）。</param>
public sealed record PresentationCategorySpec(
    string Id,
    string LabelKey,
    int Order,
    string Fold,
    IReadOnlyList<string> Classes)
{
    /// <summary>默认展开。</summary>
    public const string FoldOpen = "open";

    /// <summary>默认收起。</summary>
    public const string FoldClosed = "closed";

    /// <summary>有异常时展开（无异常收起）。</summary>
    public const string FoldOpenOnError = "open-on-error";

    /// <summary>折叠受控词（声明校验用）。</summary>
    public static readonly IReadOnlyList<string> Folds = new[] { FoldOpen, FoldClosed, FoldOpenOnError };

    /// <summary>已解析文案（宿主按 labels.json 解析；空 ⇒ 回退标识，可见不静默）。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>段渲染形态（受控词；缺省 <see cref="RenderBanner"/>）。</summary>
    public string Render { get; init; } = RenderBanner;

    /// <summary>段内行是否折叠为摘要行（缺省 false）。</summary>
    public bool RowFold { get; init; }

    /// <summary>段横幅（单行摘要，收起不渲染段体）。</summary>
    public const string RenderBanner = "banner";

    /// <summary>流式段（不建组节点，行直接挂父级）。</summary>
    public const string RenderStream = "stream";

    /// <summary>平铺段（保留逐字段行，不合成横幅）。</summary>
    public const string RenderPlain = "plain";

    /// <summary>段渲染形态受控词（声明校验用）。</summary>
    public static readonly IReadOnlyList<string> Renders = new[] { RenderBanner, RenderStream, RenderPlain };

    /// <summary>是否默认展开。</summary>
    public bool OpensByDefault => string.Equals(Fold, FoldOpen, StringComparison.Ordinal);

    /// <summary>是否「有异常才展开」。</summary>
    public bool OpensOnError => string.Equals(Fold, FoldOpenOnError, StringComparison.Ordinal);
}

/// <summary>
/// **呈现声明**（唯一策略来源，产品侧声明式给出）：呈现类清单 + 事实到类的映射。
/// <para>
/// 框架只提供机制（本类型 + <see cref="PresentationResolver"/> + 结构面判定），
/// **不认识任何具体词**（answer/bubble/process… 全部由宿主声明）。
/// </para>
/// <para>声明缺失 ⇒ <see cref="Empty"/>：所有行落 <see cref="UnknownId"/>（可见占位，不猜、不成气泡）。</para>
/// </summary>
public sealed record PresentationDeclaration(
    IReadOnlyDictionary<string, PresentationClassSpec> Classes,
    string TerminalClass,
    string OperationChannelClass,
    string ProcessChannelClass,
    IReadOnlyDictionary<ConversationRole, string> RoleFallback)
{
    /// <summary>未知类标识（结构占位；不是领域词）。</summary>
    public const string UnknownId = "unknown";

    /// <summary>组节点呈现类（facts.groupClass；缺省 process）。</summary>
    public string GroupClass { get; init; } = "process";

    /// <summary>分组模式：<see cref="GroupingNone"/>（不按分类分段）| <see cref="GroupingContinuousCategory"/>（连续同类成段）。</summary>
    public string GroupingMode { get; init; } = GroupingNone;

    /// <summary>不按分类分段（零回归位）。</summary>
    public const string GroupingNone = "none";

    /// <summary>连续同类成段：类切换即开新段（保全局时序）。</summary>
    public const string GroupingContinuousCategory = "continuous-category";

    /// <summary>分组受控词（声明校验用）。</summary>
    public static readonly IReadOnlyList<string> GroupingModes = new[] { GroupingNone, GroupingContinuousCategory };

    /// <summary>行折叠摘要截断上限（字符数；0 = 不截断）。</summary>
    public int RowFoldMaxChars { get; init; } = DefaultRowFoldMaxChars;

    /// <summary>行折叠摘要缺省截断上限。</summary>
    public const int DefaultRowFoldMaxChars = 120;

    /// <summary>分类声明（class → category 的唯一来源；空 ⇒ 分类能力未声明）。</summary>
    public IReadOnlyDictionary<string, PresentationCategorySpec> Categories { get; init; }
        = new Dictionary<string, PresentationCategorySpec>(StringComparer.Ordinal);

    /// <summary>空声明（声明不可用）：无类、无映射。</summary>
    public static readonly PresentationDeclaration Empty = new(
        new Dictionary<string, PresentationClassSpec>(StringComparer.Ordinal),
        string.Empty,
        string.Empty,
        string.Empty,
        new Dictionary<ConversationRole, string>());

    /// <summary>取呈现类规格（未声明 ⇒ null）。</summary>
    /// <param name="id">呈现类标识。</param>
    public PresentationClassSpec? Of(string? id)
        => id is not null && Classes.TryGetValue(id, out var spec) ? spec : null;

    /// <summary>结构面（未声明/未知 ⇒ <see cref="PresentationSurface.Unknown"/>：可见占位，不冒充过程/气泡）。</summary>
    /// <param name="id">呈现类标识。</param>
    public PresentationSurface SurfaceOf(string? id)
        => Of(id)?.Surface ?? PresentationSurface.Unknown;

    /// <summary>行模板键（未声明 ⇒ 宿主 fallback）。</summary>
    /// <param name="id">呈现类标识。</param>
    /// <param name="fallback">缺省模板键。</param>
    public string TemplateOf(string? id, string fallback)
        => Of(id)?.Template is { Length: > 0 } template ? template : fallback;

    /// <summary>样式 token（未声明 ⇒ 面名，便于可见降级）。</summary>
    /// <param name="id">呈现类标识。</param>
    public string StyleOf(string? id)
        => Of(id)?.StyleToken is { Length: > 0 } style ? style : SurfaceOf(id).ToString().ToLowerInvariant();

    /// <summary>是否气泡面（只有气泡面可以成为锚点）。</summary>
    /// <param name="id">呈现类标识。</param>
    public bool IsBubbleSurface(string? id) => SurfaceOf(id) == PresentationSurface.Bubble;

    /// <summary>是否显示来源角标（声明位；null/false ⇒ 不显示）。</summary>
    /// <param name="id">呈现类标识。</param>
    public bool HasAnswerBadge(string? id) => Of(id)?.AnswerBadge == true;

    /// <summary>强调 token（未声明 ⇒ null）。</summary>
    /// <param name="id">呈现类标识。</param>
    public string? EmphasisOf(string? id) => Of(id)?.Emphasis;

    /// <summary>行摘要主字段（未声明/未知 ⇒ <see cref="PresentationClassSpec.PrimaryFirstField"/>）。</summary>
    /// <param name="id">呈现类标识。</param>
    public string PrimaryOf(string? id)
        => Of(id)?.Primary is { Length: > 0 } primary ? primary : PresentationClassSpec.PrimaryFirstField;

    /// <summary>行形态是否气泡（未声明 ⇒ false：普通行）。</summary>
    /// <param name="id">呈现类标识。</param>
    public bool IsBubbleRender(string? id)
        => string.Equals(Of(id)?.Render, PresentationClassSpec.RenderBubble, StringComparison.Ordinal);

    /// <summary>段渲染形态（未声明/未知 ⇒ <see cref="PresentationCategorySpec.RenderBanner"/>）。</summary>
    /// <param name="categoryId">分类标识。</param>
    public string RenderOf(string? categoryId)
        => categoryId is not null && Categories.TryGetValue(categoryId, out var spec)
            ? spec.Render
            : PresentationCategorySpec.RenderBanner;

    /// <summary>段内行是否折叠（未声明 ⇒ false）。</summary>
    /// <param name="categoryId">分类标识。</param>
    public bool RowFoldOf(string? categoryId)
        => categoryId is not null && Categories.TryGetValue(categoryId, out var spec) && spec.RowFold;

    /// <summary>呈现类所属分类（未声明/未注册 ⇒ null：调用方须显式可见化，禁猜）。</summary>
    /// <param name="classId">呈现类标识。</param>
    public PresentationCategorySpec? CategorySpecOf(string? classId)
    {
        if (string.IsNullOrEmpty(classId)) return null;
        foreach (var spec in Categories.Values)
        {
            if (spec.Classes.Contains(classId, StringComparer.Ordinal)) return spec;
        }

        return null;
    }

    /// <summary>呈现类所属分类标识（未注册 ⇒ null）。</summary>
    /// <param name="classId">呈现类标识。</param>
    public string? CategoryOf(string? classId) => CategorySpecOf(classId)?.Id;

    /// <summary>分类文案（未解析/未声明 ⇒ 回退标识，可见不静默）。</summary>
    /// <param name="categoryId">分类标识。</param>
    public string CategoryLabel(string? categoryId)
        => categoryId is not null
           && Categories.TryGetValue(categoryId, out var spec)
           && spec.Label.Length > 0
            ? spec.Label
            : categoryId ?? string.Empty;

    /// <summary>解析分类文案（宿主装载期调用一次；loc 单源仍在 labels.json）。</summary>
    /// <param name="lookup">标签键 → 文案（返回 null/空 ⇒ 回退分类标识）。</param>
    public PresentationDeclaration ResolveCategoryLabels(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (Categories.Count == 0) return this;

        var resolved = new Dictionary<string, PresentationCategorySpec>(StringComparer.Ordinal);
        foreach (var pair in Categories)
        {
            var text = lookup(pair.Value.LabelKey);
            resolved[pair.Key] = pair.Value with
            {
                Label = string.IsNullOrWhiteSpace(text) ? pair.Key : text!,
            };
        }

        return this with { Categories = resolved };
    }

    /// <summary>声明是否可用（类清单非空）。</summary>
    public bool IsDeclared => Classes.Count > 0;

    /// <summary>
    /// 解析声明 JSON（产品声明面 → 数据）。
    /// 形制：<c>{ "classes": { "&lt;id&gt;": { "surface": "bubble|process|operation|notice", "template": "…", "style": "…" } },
    /// "facts": { "terminal": "…", "channelOperation": "…", "channelProcess": "…", "roleFallback": { "user": "…" } } }</c>
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    public static PresentationDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var classes = new Dictionary<string, PresentationClassSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("classes", out var classesElement) && classesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in classesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                var id = Normalize(property.Name);
                if (id.Length == 0) continue;

                var surface = ParseSurface(ReadString(property.Value, "surface"));
                var template = ReadString(property.Value, "template") ?? id;
                var style = ReadString(property.Value, "style") ?? id;
                var badge = ReadBool(property.Value, "answerBadge");
                var emphasis = ReadString(property.Value, "emphasis");
                var primary = Normalize(ReadString(property.Value, "primary") ?? string.Empty);
                if (!PresentationClassSpec.Primaries.Contains(primary, StringComparer.Ordinal))
                {
                    primary = PresentationClassSpec.PrimaryFirstField;
                }

                var classRender = Normalize(ReadString(property.Value, "render") ?? string.Empty);
                if (!PresentationClassSpec.Renders.Contains(classRender, StringComparer.Ordinal))
                {
                    classRender = PresentationClassSpec.RenderRow;
                }

                classes[id] = new PresentationClassSpec(id, surface, template, style, badge)
                {
                    Emphasis = emphasis is { Length: > 0 } ? Normalize(emphasis) : null,
                    Primary = primary,
                    Render = classRender,
                };
            }
        }

        var terminal = string.Empty;
        var operation = string.Empty;
        var process = string.Empty;
        var groupClass = "process";
        var roleFallback = new Dictionary<ConversationRole, string>();

        var categories = new Dictionary<string, PresentationCategorySpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("categories", out var categoryMap) && categoryMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in categoryMap.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                var id = Normalize(property.Name);
                if (id.Length == 0) continue;
                var fold = Normalize(ReadString(property.Value, "fold") ?? PresentationCategorySpec.FoldClosed);
                var categoryRender = Normalize(ReadString(property.Value, "render") ?? string.Empty);
                categories[id] = new PresentationCategorySpec(
                    id,
                    ReadString(property.Value, "labelKey")?.Trim() ?? id,
                    ReadInt(property.Value, "order", categories.Count),
                    PresentationCategorySpec.Folds.Contains(fold, StringComparer.Ordinal)
                        ? fold
                        : PresentationCategorySpec.FoldClosed,
                    ReadStringList(property.Value, "classes"))
                {
                    Render = PresentationCategorySpec.Renders.Contains(categoryRender, StringComparer.Ordinal)
                        ? categoryRender
                        : PresentationCategorySpec.RenderBanner,
                    RowFold = ReadBool(property.Value, "rowFold"),
                };
            }
        }

        var groupingMode = GroupingNone;
        var rowFoldMaxChars = DefaultRowFoldMaxChars;
        if (root.TryGetProperty("grouping", out var grouping) && grouping.ValueKind == JsonValueKind.Object)
        {
            var declaredMode = Normalize(ReadString(grouping, "mode") ?? GroupingNone);
            groupingMode = GroupingModes.Contains(declaredMode, StringComparer.Ordinal) ? declaredMode : GroupingNone;
            if (grouping.TryGetProperty("rowFold", out var rowFold) && rowFold.ValueKind == JsonValueKind.Object)
            {
                rowFoldMaxChars = ReadInt(rowFold, "maxChars", DefaultRowFoldMaxChars);
            }
        }

        if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object)
        {
            terminal = Normalize(ReadString(facts, "terminal") ?? string.Empty);
            operation = Normalize(ReadString(facts, "channelOperation") ?? string.Empty);
            process = Normalize(ReadString(facts, "channelProcess") ?? string.Empty);
            groupClass = ReadString(facts, "groupClass") is { Length: > 0 } declaredGroup ? Normalize(declaredGroup) : groupClass;

            if (facts.TryGetProperty("roleFallback", out var roles) && roles.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in roles.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    if (!Enum.TryParse<ConversationRole>(property.Name, ignoreCase: true, out var role)) continue;
                    var id = Normalize(property.Value.GetString() ?? string.Empty);
                    if (id.Length > 0) roleFallback[role] = id;
                }
            }
        }

        return new PresentationDeclaration(classes, terminal, operation, process, roleFallback)
        {
            GroupClass = groupClass,
            Categories = categories,
            GroupingMode = groupingMode,
            RowFoldMaxChars = rowFoldMaxChars,
        };
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.Number
           && property.TryGetInt32(out var value)
            ? value
            : fallback;

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
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value!.Trim());
        }

        return values;
    }

    private static PresentationSurface ParseSurface(string? word)
        => word?.Trim().ToLowerInvariant() switch
        {
            "bubble" => PresentationSurface.Bubble,
            "operation" => PresentationSurface.Operation,
            "notice" => PresentationSurface.Notice,
            _ => PresentationSurface.Process,
        };
}
