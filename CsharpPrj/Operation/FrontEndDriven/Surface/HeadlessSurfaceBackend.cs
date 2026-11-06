#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.Surface;

/// <summary>无头后端投影器：<see cref="RenderPlan"/> → JSON 快照（供门禁/契约测试断言覆盖率与节点数）。</summary>
public sealed class HeadlessSurfaceBackend : IBackendProjector<string>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>后端 id（测试/门禁用；不参与载体映射）。</summary>
    public string BackendId => "headless";

    private readonly UiDeclaration? _declaration;
    private readonly string? _skin;

    /// <param name="declaration">UI 个性化声明（可空 ⇒ 快照不含主题段）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    public HeadlessSurfaceBackend(UiDeclaration? declaration = null, string? skin = null)
    {
        _declaration = declaration;
        _skin = skin;
    }

    /// <inheritdoc />
    public IReadOnlyList<UnsupportedSlot> UnsupportedCells => Array.Empty<UnsupportedSlot>();

    /// <inheritdoc />
    public string Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var theme = _declaration?.Resolve(_skin);
        var snapshot = new
        {
            schema = "surface-snapshot-v1",
            view = plan.ViewKey,
            nodeCount = plan.NodeCount,
            fieldCount = plan.FieldCount,
            unknownFieldCount = plan.UnknownFieldCount,
            theme = theme is null
                ? null
                : new { skin = theme.Skin, density = theme.Density, fingerprint = _declaration!.Fingerprint },
            roots = ToNodes(plan.Roots, theme),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    private static List<object> ToNodes(IReadOnlyList<UiSurfaceNode> nodes, ResolvedUi? theme)
    {
        var list = new List<object>(nodes.Count);
        foreach (var node in nodes)
        {
            var fields = new List<object>(node.Fields.Count);
            foreach (var field in node.Fields)
            {
                fields.Add(new
                {
                    name = field.Name,
                    cell = field.Cell,
                    label = field.Label,
                    unknown = field.IsUnknown,
                    value = FieldTextFormatter.Format(field),
                });
            }

            list.Add(new
            {
                role = node.Role,
                key = node.Key,
                @class = node.Class,
                part = theme?.PartForRole(node.Role)?.Name,
                surface = node.Surface.ToString(),
                state = node.State.ToString(),
                actions = node.Actions,
                fields,
                children = ToNodes(node.Children, theme),
            });
        }

        return list;
    }
}
