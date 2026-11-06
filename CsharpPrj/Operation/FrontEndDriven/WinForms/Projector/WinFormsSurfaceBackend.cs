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
    private readonly HashSet<string> _unsupported = new(StringComparer.Ordinal);

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键）。</param>
    public WinFormsSurfaceBackend(SurfaceMapDeclaration map, string backendId = "winforms")
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
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
        foreach (var node in plan.Roots)
        {
            ProjectNode(node, null, 0, snapshot, ref counter);
        }

        return snapshot;
    }

    private void ProjectNode(UiSurfaceNode node, string? parentId, int order, WinFormSnapshot snapshot, ref int counter)
    {
        var control = ControlType(node.Role);
        if (control is null)
        {
            _unsupported.Add(node.Role);
            return;
        }

        counter++;
        var id = $"{node.Key}#{counter.ToString(CultureInfo.InvariantCulture)}";
        snapshot.Controls.Add(new ControlDescriptor
        {
            Id = id,
            Name = node.Key,
            Type = control,
            Text = $"{node.Class}",
            Visible = true,
            Enabled = node.State != UiNodeState.Disabled,
            Dock = "Top",
        });

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
            snapshot.Controls.Add(new ControlDescriptor
            {
                Id = fieldId,
                Name = field.Name,
                Type = fieldControl,
                Text = $"{field.Label ?? field.Name}: {TextOf(field)}",
                Visible = true,
                Enabled = true,
                Dock = "Top",
            });
            snapshot.Relations.Add(new ChildRelation { ParentId = id, ChildId = fieldId, OrderIndex = childOrder++ });
        }

        foreach (var child in node.Children)
        {
            ProjectNode(child, id, childOrder++, snapshot, ref counter);
        }
    }

    private string? ControlType(string key)
        => _map.Props(BackendId, key) is { } props && props.TryGetValue("control", out var control)
            && !string.IsNullOrWhiteSpace(control)
            ? control
            : null;

    private static string TextOf(UiField field)
        => field.IsUnknown || field.Value is null
            ? "?"
            : field.Value is string text ? text : field.Value.ToString() ?? "?";
}
