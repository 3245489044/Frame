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
    private readonly HashSet<string> _unsupported = new(StringComparer.Ordinal);

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键）。</param>
    /// <param name="declaration">UI 个性化声明（可空 ⇒ 仅结构映射，零行为变化）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    public WinFormsSurfaceBackend(
        SurfaceMapDeclaration map,
        string backendId = "winforms",
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
    public IReadOnlyList<string> UnsupportedCells => _unsupported.ToList();

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
            _unsupported.Add(node.Role);
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
        ApplyTheme(descriptor, theme?.PartForRole(node.Role) ?? theme?.PartForClass(node.Class), node.State, node.Role);
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
                _unsupported.Add(field.Cell);
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
            ApplyTheme(fieldDescriptor, theme?.PartForCell(field.Cell), UiNodeState.Open, field.Cell);
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
    /// </summary>
    private void ApplyTheme(ControlDescriptor descriptor, ResolvedPart? part, UiNodeState state, string key)
    {
        if (part is null) return;

        var stateId = state switch
        {
            UiNodeState.Closed => "closed",
            UiNodeState.Disabled => "disabled",
            _ => null,
        };

        foreach (var visual in part.Visuals(BackendId, stateId))
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
                    else _unsupported.Add($"{key}.{visual.Slot}:size");
                    break;
                default:
                    _unsupported.Add($"{key}.{visual.Slot}:{visual.Prop}");
                    break;
            }
        }

        foreach (var slot in part.MissingSlots(BackendId, stateId)) _unsupported.Add($"{key}.{slot}");
    }

    /// <summary>声明值（可带单位）→ 控件字号；解析失败 ⇒ 可见降级（禁静默默认）。</summary>
    private static bool TryParseSize(string value, out float size)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == '-') buffer.Append(ch);
            else break;
        }

        return float.TryParse(buffer.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out size);
    }

    private static string TextOf(UiField field)
        => field.IsUnknown || field.Value is null
            ? "?"
            : field.Value is string text ? text : field.Value.ToString() ?? "?";
}
