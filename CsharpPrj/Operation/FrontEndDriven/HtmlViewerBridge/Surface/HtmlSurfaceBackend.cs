#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering.Surface;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>
/// HTML 后端投影器：<see cref="RenderPlan"/> → <see cref="UiNode"/> 树。
/// <para>标签/样式一律取自载体映射声明（代码零载体字面量）；缺映射 ⇒ 可见降级文本节点。</para>
/// </summary>
public sealed class HtmlSurfaceBackend : IBackendProjector<UiNode>
{
    private readonly SurfaceMapDeclaration _map;
    private readonly UiDeclaration? _declaration;
    private readonly string? _skin;
    private readonly Dictionary<string, string> _unsupported = new(StringComparer.Ordinal);

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键；缺省 = 声明内 html 键）。</param>
    /// <param name="declaration">UI 个性化声明（可空 ⇒ 仅结构映射，零行为变化）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    public HtmlSurfaceBackend(
        SurfaceMapDeclaration map,
        string backendId = "html",
        UiDeclaration? declaration = null,
        string? skin = null)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _declaration = declaration;
        _skin = skin;
        BackendId = backendId;
    }

    /// <inheritdoc />
    public string BackendId { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> UnsupportedCells => _unsupported.Keys.ToList();

    /// <inheritdoc />
    public UiNode Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var theme = _declaration?.Resolve(_skin);
        return UiNode.Element(
            string.Empty,
            plan.ViewKey,
            null,
            plan.Roots.Select(node => ProjectNode(node, theme)).ToList());
    }

    private UiNode ProjectNode(UiSurfaceNode node, ResolvedUi? theme)
    {
        var props = _map.Props(BackendId, node.Role);
        if (props is null || !props.TryGetValue("tag", out var tag) || string.IsNullOrWhiteSpace(tag))
        {
            _unsupported[SurfaceRoleIds.Unknown] = "role";
            return UiNode.TextNode($"?{node.Role}");
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["data-role"] = node.Role,
            ["data-key"] = node.Key,
        };
        if (props.TryGetValue("cls", out var cls) && cls.Length > 0) attributes["class"] = cls;
        if (node.Surface != PresentationSurface.Unknown)
        {
            attributes["data-surface"] = node.Surface.ToString().ToLowerInvariant();
        }

        ApplyTheme(attributes, theme?.PartForRole(node.Role) ?? theme?.PartForClass(node.Class), node.State);

        var children = new List<UiNode>(node.Fields.Count + node.Children.Count);
        foreach (var field in node.Fields) children.Add(ProjectField(field, theme));
        foreach (var child in node.Children) children.Add(ProjectNode(child, theme));

        return new UiNode(tag, node.Key, attributes, null, children, null);
    }

    private UiNode ProjectField(UiField field, ResolvedUi? theme)
    {
        var props = _map.Props(BackendId, field.Cell);
        if (props is null || !props.TryGetValue("tag", out var tag) || string.IsNullOrWhiteSpace(tag))
        {
            _unsupported[field.Cell] = field.Name;
            return UiNode.TextNode(field.IsUnknown ? $"?{field.Name}" : TextOf(field));
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["data-field"] = field.Name,
            ["data-cell"] = field.Cell,
        };
        if (props.TryGetValue("cls", out var cls) && cls.Length > 0) attributes["class"] = cls;
        if (field.IsUnknown) attributes["data-unknown"] = "1";

        ApplyTheme(attributes, theme?.PartForCell(field.Cell), UiNodeState.Open);

        return new UiNode(tag, $"{field.Name}", attributes, TextOf(field), null, null);
    }

    /// <summary>
    /// 应用声明式主题：结构类（声明 cls）→ class；槽位绑定 cssvar → style；无法承载的槽位 → 可见标记。
    /// </summary>
    private void ApplyTheme(IDictionary<string, string> attributes, ResolvedPart? part, UiNodeState state)
    {
        if (part is null) return;

        var structure = part.Structure(BackendId);
        if (structure is not null && structure.TryGetValue("cls", out var cls)
            && cls.Length > 0 && !attributes.ContainsKey("class"))
        {
            attributes["class"] = cls;
        }

        var stateId = StateId(state);
        var style = new List<string>();
        foreach (var visual in part.Visuals(BackendId, stateId))
        {
            if (!string.Equals(visual.Bind, "cssvar", StringComparison.Ordinal)) continue;
            style.Add($"{visual.Prop}:{visual.Value}");
        }

        if (style.Count > 0) attributes["style"] = string.Join(";", style);
        attributes["data-part"] = part.Name;

        var missing = part.MissingSlots(BackendId, stateId);
        if (missing.Count > 0) attributes["data-unsupported"] = string.Join(",", missing);
    }

    private static string? StateId(UiNodeState state) => state switch
    {
        UiNodeState.Closed => "closed",
        UiNodeState.Disabled => "disabled",
        _ => null,
    };

    /// <summary>字段值 → 文本（唯一合成点；未知占位走可见标记）。</summary>
    /// <param name="field">字段。</param>
    public static string TextOf(UiField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.IsUnknown) return "?";
        return field.Value switch
        {
            null => "?",
            string text => text,
            bool flag => flag ? "true" : "false",
            IEnumerable<KeyValuePair<string, string>> pairs => string.Join("; ", pairs.Select(p => $"{p.Key}={p.Value}")),
            System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>()),
            _ => field.Value.ToString() ?? "?",
        };
    }
}
