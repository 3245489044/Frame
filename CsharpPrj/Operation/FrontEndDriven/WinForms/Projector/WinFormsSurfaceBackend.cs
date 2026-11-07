#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrontEndDriven.WinForms.Applier;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Projector;

/// <summary>
/// WinForms 后端投影器：<see cref="RenderPlan"/> → <see cref="WinFormSnapshot"/>（控件族取自载体映射声明）。
/// <para>缺映射的单元记入 <see cref="UnsupportedCells"/>（可见降级；禁静默丢字段）。</para>
/// </summary>
public sealed class WinFormsSurfaceBackend : IBackendProjector<WinFormSnapshot>
{
    private readonly SurfaceMapDeclaration _map;
    private readonly UiDeclaration? _declaration;
    private readonly string? _skin;
    private readonly ResolvedSurface? _surface;
    private readonly HashSet<UnsupportedSlot> _unsupported = new();

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键）。</param>
    /// <param name="declaration">UI 个性化声明（可空 ⇒ 仅结构映射，零行为变化）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    /// <param name="surface">已解析表面（可空 ⇒ 原 ControlConfig 路径）。</param>
    public WinFormsSurfaceBackend(
        SurfaceMapDeclaration map,
        string backendId = "winforms",
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
    public WinFormSnapshot Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var snapshot = new WinFormSnapshot();
        var counter = 0;
        var theme = _declaration?.Resolve(_skin);
        foreach (var node in plan.Roots)
        {
            ProjectNode(node, null, 0, snapshot, ref counter, theme);
        }

        return snapshot;
    }

    private void ProjectNode(
        UiSurfaceNode node, string? parentId, int order, WinFormSnapshot snapshot, ref int counter, ResolvedUi? theme)
    {
        var control = ControlType(node.Role);
        if (control is null)
        {
            _unsupported.Add(new UnsupportedSlot(BackendId, node.Role, string.Empty, "missing-structure"));
            return;
        }

        counter++;
        var id = $"{node.Key}#{counter.ToString(CultureInfo.InvariantCulture)}";
        var descriptor = new ControlDescriptor
        {
            Id = id,
            Name = node.Key,
            Type = control,
            Text = $"{node.Class}",
            Visible = true,
            Enabled = node.State != UiNodeState.Disabled,
            Dock = "Top",
        };
        // M5a：容器结构（控件类型/Dock）与已解析材质引用；不在此层做图元绘制（M5b）。
        if (_surface?.Container(node.Key) is { } container)
        {
            if (container.Structure.TryGetValue("control", out var structureControl)
                && !string.IsNullOrWhiteSpace(structureControl))
            {
                descriptor.Type = structureControl;
            }

            if (container.Structure.TryGetValue("dock", out var structureDock)
                && !string.IsNullOrWhiteSpace(structureDock))
            {
                descriptor.Dock = structureDock;
                descriptor.HasDock = true;
            }

            descriptor.SkinRefs = container.Materials;
            descriptor.Issues = container.Issues;
        }

        ApplyTheme(descriptor, theme?.ThemeFor(node.Role, node.Class), node.State, node.Role);
        snapshot.Controls.Add(descriptor);

        if (parentId is not null)
        {
            snapshot.Relations.Add(new ChildRelation
            {
                ParentId = parentId,
                ChildId = id,
                OrderIndex = order,
            });
        }

        var childOrder = 0;
        foreach (var field in node.Fields)
        {
            var fieldControl = ControlType(field.Cell);
            if (fieldControl is null)
            {
                _unsupported.Add(new UnsupportedSlot(BackendId, field.Cell, string.Empty, "missing-structure"));
                continue;
            }

            counter++;
            var fieldId = $"{id}/{field.Name}#{counter.ToString(CultureInfo.InvariantCulture)}";
            var fieldDescriptor = new ControlDescriptor
            {
                Id = fieldId,
                Name = field.Name,
                Type = fieldControl,
                Text = $"{field.Label ?? field.Name}: {TextOf(field)}",
                Visible = true,
                Enabled = true,
                Dock = "Top",
            };
            ApplyTheme(fieldDescriptor, theme?.ThemeForCell(field.Cell), UiNodeState.Open, field.Cell);
            snapshot.Controls.Add(fieldDescriptor);
            snapshot.Relations.Add(new ChildRelation { ParentId = id, ChildId = fieldId, OrderIndex = childOrder++ });
        }

        foreach (var child in node.Children)
        {
            ProjectNode(child, id, childOrder++, snapshot, ref counter, theme);
        }
    }

    private string? ControlType(string key)
        => _map.Props(BackendId, key) is { } props && props.TryGetValue("control", out var control)
            && !string.IsNullOrWhiteSpace(control)
            ? control
            : null;

    /// <summary>
    /// 应用声明式主题：槽位绑定 prop → 控件属性；未支持属性/槽位 → <see cref="UnsupportedCells"/>（可见降级）。
    /// <para>结构来自角色部件、外观来自呈现类部件（合并由 <see cref="UiTheme"/> 单点裁决）。</para>
    /// </summary>
    private void ApplyTheme(ControlDescriptor descriptor, UiTheme? theme, UiNodeState state, string key)
    {
        if (theme is null) return;

        var stateId = state switch
        {
            UiNodeState.Closed => "closed",
            UiNodeState.Disabled => "disabled",
            _ => null,
        };

        foreach (var visual in theme.Visuals(BackendId, stateId))
        {
            if (!string.Equals(visual.Bind, "prop", StringComparison.Ordinal)) continue;
            switch (visual.Prop)
            {
                case "ForeColor":
                    descriptor.ForeColor = visual.Value;
                    break;
                case "BackColor":
                    descriptor.BackColor = visual.Value;
                    break;
                case "FontFamily":
                    descriptor.FontFamily = visual.Value;
                    break;
                case "FontSize":
                    if (TryParseSize(visual.Value, out var size)) descriptor.FontSize = size;
                    else _unsupported.Add(new UnsupportedSlot(BackendId, key, visual.Slot, "size"));
                    break;
                default:
                    _unsupported.Add(new UnsupportedSlot(BackendId, key, visual.Slot, visual.Prop));
                    break;
            }
        }

        foreach (var slot in theme.MissingSlots(BackendId, stateId)) _unsupported.Add(new UnsupportedSlot(BackendId, key, slot, "token-missing"));
    }

    /// <summary>声明值（单位less）→ 控件字号；解析失败 ⇒ 可见降级（禁静默默认）。</summary>
    private static bool TryParseSize(string value, out float size)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out size);

    private static string TextOf(UiField field) => FieldTextFormatter.Format(field);
}
