#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering.Surface;
using UiBackend;
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
        var isFold = string.Equals(node.Role, SurfaceRoleIds.Fold, StringComparison.Ordinal);
        var props = _map.Props(BackendId, node.Role);
        var tag = string.Empty;
        var declaredTag = string.Empty;
        var hasStructure = props is not null
                           && props.TryGetValue("tag", out declaredTag)
                           && !string.IsNullOrWhiteSpace(declaredTag);
        if (hasStructure)
        {
            tag = declaredTag;
        }

        if (!hasStructure)
        {
            if (!isFold)
            {
                _unsupported.Add(new UnsupportedSlot(BackendId, SurfaceRoleIds.Unknown, string.Empty, "missing-structure"));
                return UiNode.TextNode($"{SurfaceGlyphs.MissingStructure}:{node.Role}");
            }

            // 折叠容器的载体族固定为 HTML 原生折叠；映射缺省不构成结构缺失。
            tag = "details";
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["data-role"] = node.Role,
            ["data-key"] = node.Key,
        };
        if (props is not null && props.TryGetValue("cls", out var cls) && cls.Length > 0)
        {
            attributes["class"] = cls;
        }
        if (node.Surface != PresentationSurface.Unknown)
        {
            attributes["data-surface"] = node.Surface.ToString().ToLowerInvariant();
        }
        if (node.Category is { Length: > 0 } category)
        {
            attributes["data-category"] = category;
        }
        if (node.Style is { Length: > 0 } styleName)
        {
            attributes["data-style"] = styleName;
        }
        if (node.FoldKind is { Length: > 0 } foldKind)
        {
            attributes["data-fold-kind"] = foldKind;
        }
        if (node.Facts is { Count: > 0 } facts)
        {
            foreach (var fact in facts)
            {
                if (fact.Key.Length == 0) continue;
                attributes["data-fact-" + fact.Key] = fact.Value;
            }
        }

        if (isFold)
        {
            attributes["data-fold"] = "1";
            if (node.State == UiNodeState.Open) attributes["open"] = "open";
            else attributes["data-fold-state"] = "closed";
        }

        // M4a：容器结构/材质类来自已解析表面（可空 ⇒ 原路径）；仅新增 class/data 属性，不改段键。
        tag = ApplySurface(attributes, node.Key, tag);
        ApplyTheme(attributes, theme?.ThemeFor(node.Role, node.Class), node.State);

        var isBanner = string.Equals(node.FoldKind, FrameComposer.BannerFoldKind, StringComparison.Ordinal);

        var children = new List<UiNode>(node.Fields.Count + node.Children.Count + 1);
        if (isFold)
        {
            var summaryProps = _map.Props(BackendId, "summary");
            var summaryTag = summaryProps is not null && summaryProps.TryGetValue("tag", out var declaredSummary)
                             && !string.IsNullOrWhiteSpace(declaredSummary)
                ? declaredSummary
                : "summary";
            var summaryClass = summaryProps is not null && summaryProps.TryGetValue("cls", out var summaryCls)
                ? summaryCls
                : "fold-summary";
            var label = node.Fields.Count > 0 ? FieldTextFormatter.Format(node.Fields[0]) : node.Class;
            children.Add(new UiNode(
                summaryTag,
                $"{node.Key}:summary",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = summaryClass },
                SummaryOf(label, node.SummaryMaxChars),
                null,
                null));
        }

        // 段体恒产出：收起只是载体隐藏（<details> 原生语义），展开必须能拿到完整正文。
        var body = new List<UiNode>(node.Fields.Count + node.Children.Count);
        for (var i = 0; i < node.Fields.Count; i++)
        {
            // banner 段头已消费首字段；行折叠的段体保留主字段全文（展开无损）。
            if (isFold && isBanner && i == 0) continue;
            var field = node.Fields[i];
            if (field.IsNotApplicable) continue;
            body.Add(ProjectField(field, theme));
        }

        foreach (var child in node.Children) body.Add(ProjectNode(child, theme));

        if (isFold && body.Count > 0)
        {
            var bodyProps = _map.Props(BackendId, "foldBody");
            var bodyTag = bodyProps is not null && bodyProps.TryGetValue("tag", out var declaredBody)
                          && !string.IsNullOrWhiteSpace(declaredBody)
                ? declaredBody
                : "div";
            var bodyClass = bodyProps is not null && bodyProps.TryGetValue("cls", out var bodyCls)
                ? bodyCls
                : "fold-body";
            children.Add(new UiNode(
                bodyTag,
                $"{node.Key}:body",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["class"] = bodyClass,
                    ["data-role"] = "foldBody",
                },
                null,
                body,
                null));
        }
        else
        {
            children.AddRange(body);
        }

        return new UiNode(tag, node.Key, attributes, null, children, null);
    }

    /// <summary>折叠摘要：取首行、按声明上限截断（0 = 不截断）；不改变段体全文。</summary>
    private static string SummaryOf(string text, int maxChars)
    {
        if (maxChars <= 0) return text;
        var line = text;
        var breakAt = line.IndexOfAny(new[] { '\r', '\n' });
        if (breakAt >= 0) line = line[..breakAt];
        return line.Length > maxChars ? line[..maxChars] + "…" : line;
    }

    private UiNode ProjectField(UiField field, ResolvedUi? theme)
    {
        var props = _map.Props(BackendId, field.Cell);
        if (props is null || !props.TryGetValue("tag", out var tag) || string.IsNullOrWhiteSpace(tag))
        {
            _unsupported.Add(new UnsupportedSlot(BackendId, field.Cell, string.Empty, "missing-structure"));
            return UiNode.TextNode($"{SurfaceGlyphs.MissingStructure}:{field.Name}={TextOf(field)}");
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["data-field"] = field.Name,
            ["data-cell"] = field.Cell,
        };
        if (props.TryGetValue("cls", out var cls) && cls.Length > 0) attributes["class"] = cls;
        if (field.IsUnknown)
        {
            attributes["data-unknown"] = "1";
            attributes["data-unknown-reason"] = field.UnknownReason ?? "missing-value";
        }

        ApplyTheme(attributes, theme?.ThemeForCell(field.Cell), UiNodeState.Open);

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
    /// 应用声明式主题：结构类（角色部件 cls）→ class；槽位绑定 cssvar → style；无法承载的槽位 → 可见标记。
    /// <para>结构来自角色部件，外观来自呈现类部件（合并由 <see cref="UiTheme"/> 单点裁决）。</para>
    /// </summary>
    private void ApplyTheme(IDictionary<string, string> attributes, UiTheme? theme, UiNodeState state)
    {
        if (theme is null) return;

        var structure = theme.Structure(BackendId);
        if (structure is not null && structure.TryGetValue("cls", out var cls) && cls.Length > 0)
        {
            // 4 类来源合并（映射 cls / 容器结构 cls / 材质类 / 声明 cls）：声明 cls 不再被静默丢弃。
            // 类选择器具体性相同，词序不改变级联结果；重复词只去重，不覆盖。
            var existing = attributes.TryGetValue("class", out var current) ? current : string.Empty;
            attributes["class"] = string.Join(" ",
                (existing + " " + cls).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.Ordinal));
        }

        var stateId = StateId(state);
        var style = new List<string>();
        foreach (var visual in theme.Visuals(BackendId, stateId))
        {
            if (!string.Equals(visual.Bind, "cssvar", StringComparison.Ordinal)) continue;
            style.Add($"{visual.Prop}:{CssValue.AppendUnit(visual.Value, visual.Unit)}");
        }

        if (style.Count > 0)
        {
            // style 合并（不整体覆盖）：上游映射/模板已写的同名键由声明覆盖，其余保留（E-13 修复）。
            attributes["style"] = CssValue.MergeStyle(
                attributes.TryGetValue("style", out var existingStyle) ? existingStyle : null,
                style);
        }
        if (theme.Name is { Length: > 0 } partName) attributes["data-part"] = partName;

        // 槽位缺口只保留 DOM 属性（单一表示：供诊断读取；不进 UnsupportedCells）。
        var missing = theme.MissingSlots(BackendId, stateId);
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
    public static string TextOf(UiField field)
        => FieldTextFormatter.Format(field, SurfaceGlyphs.Unknown);
}
