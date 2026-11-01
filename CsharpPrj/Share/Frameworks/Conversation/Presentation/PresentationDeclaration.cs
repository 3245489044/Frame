#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Presentation;

/// <summary>呈现**结构面**（框架级通用概念：气泡面 / 过程面 / 操作面 / 通知面）。</summary>
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
    bool AnswerBadge = false);


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

    /// <summary>结构面（未声明/未知 ⇒ Process：可见、不冒充气泡）。</summary>
    /// <param name="id">呈现类标识。</param>
    public PresentationSurface SurfaceOf(string? id)
        => Of(id)?.Surface ?? PresentationSurface.Process;

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
                classes[id] = new PresentationClassSpec(id, surface, template, style, badge);
            }
        }

        var terminal = string.Empty;
        var operation = string.Empty;
        var process = string.Empty;
        var roleFallback = new Dictionary<ConversationRole, string>();

        if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object)
        {
            terminal = Normalize(ReadString(facts, "terminal") ?? string.Empty);
            operation = Normalize(ReadString(facts, "channelOperation") ?? string.Empty);
            process = Normalize(ReadString(facts, "channelProcess") ?? string.Empty);

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

        return new PresentationDeclaration(classes, terminal, operation, process, roleFallback);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static PresentationSurface ParseSurface(string? word)
        => word?.Trim().ToLowerInvariant() switch
        {
            "bubble" => PresentationSurface.Bubble,
            "operation" => PresentationSurface.Operation,
            "notice" => PresentationSurface.Notice,
            _ => PresentationSurface.Process,
        };
}
