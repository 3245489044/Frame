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
    private readonly ResolvedSurface? _surface;
    private readonly HashSet<UnsupportedSlot> _unsupported = new();

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键；缺省 = 声明内 html 键）。</param>
    /// <param name="declaration">UI 个性化声明（可空 ⇒ 仅结构映射，零行为变化）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    /// <param name="surface">已解析表面（可空 ⇒ 走原模板路径，零回归）。</param>
    public HtmlSurfaceBackend(
        SurfaceMapDeclaration map,
        string backendId = "html",
        UiDeclaration? declaration = null,
        string? skin = null,
        ResolvedSurface? surface = null)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _declaration = declaration;
        _skin = skin;
        _surface = surface;
        BackendId = backendId;
    }

    /// <inheritdoc />
    public string BackendId { get; }

    /// <inheritdoc />
    public IReadOnlyList<UnsupportedSlot> UnsupportedCells => _unsupported.ToList();

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
            _unsupported.Add(new UnsupportedSlot(BackendId, SurfaceRoleIds.Unknown, string.Empty, "missing-structure"));
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

        // M4a：容器结构/材质类来自已解析表面（可空 ⇒ 原路径）；仅新增 class/data 属性，不改段键。
        tag = ApplySurface(attributes, node.Key, tag);
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
            _unsupported.Add(new UnsupportedSlot(BackendId, field.Cell, string.Empty, "missing-structure"));
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
    /// 叠加已解析表面的容器结构与材质类：tag/cls 来自容器映射，材质类 = <c>material-&lt;id&gt;</c>。
    /// <para>仅新增属性；未命中容器 ⇒ 原样返回（可见降级由 SurfaceProgram 门禁承担）。</para>
    /// </summary>
    private string ApplySurface(IDictionary<string, string> attributes, string key, string tag)
    {
        if (_surface?.Container(key) is not { } container) return tag;

        if (container.Structure.TryGetValue("tag", out var structureTag) && structureTag.Length > 0)
        {
            tag = structureTag;
        }

        var classes = new List<string>();
        if (attributes.TryGetValue("class", out var existing) && existing.Length > 0) classes.Add(existing);
        if (container.Structure.TryGetValue("cls", out var structureClass) && structureClass.Length > 0)
        {
            classes.Add(structureClass);
        }

        foreach (var material in container.Materials.Values)
        {
            classes.Add(CssSurfaceProjector.ClassName(material.FallbackUsed ?? material.MaterialId));
        }

        if (classes.Count > 0)
        {
            attributes["class"] = string.Join(" ", classes.Distinct(StringComparer.Ordinal));
        }

        return tag;
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
            style.Add($"{visual.Prop}:{WithUnit(visual.Value, visual.Unit)}");
        }

        if (style.Count > 0) attributes["style"] = string.Join(";", style);
        attributes["data-part"] = part.Name;

        // 槽位缺口只保留 DOM 属性（单一表示：供诊断读取；不进 UnsupportedCells）。
        var missing = part.MissingSlots(BackendId, stateId);
        if (missing.Count > 0) attributes["data-unsupported"] = string.Join(",", missing);
    }

    private static string? StateId(UiNodeState state) => state switch
    {
        UiNodeState.Closed => "closed",
        UiNodeState.Disabled => "disabled",
        _ => null,
    };

    /// <summary>字段值 → 文本（唯一合成点 = <see cref="FieldTextFormatter"/>）。</summary>
    /// <param name="field">字段。</param>
    public static string TextOf(UiField field) => FieldTextFormatter.Format(field);

    /// <summary>单位拼接：token 值单位less，载体声明单位（多值按空白分隔逐个追加）。</summary>
    private static string WithUnit(string value, string unit)
    {
        if (unit.Length == 0) return value;
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => v + unit));
    }
}
