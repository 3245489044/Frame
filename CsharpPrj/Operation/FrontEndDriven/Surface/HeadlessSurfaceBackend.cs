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

    /// <inheritdoc />
    public IReadOnlyList<string> UnsupportedCells => Array.Empty<string>();

    /// <inheritdoc />
    public string Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var snapshot = new
        {
            schema = "surface-snapshot-v1",
            view = plan.ViewKey,
            nodeCount = plan.NodeCount,
            fieldCount = plan.FieldCount,
            unknownFieldCount = plan.UnknownFieldCount,
            roots = ToNodes(plan.Roots),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    private static List<object> ToNodes(IReadOnlyList<UiSurfaceNode> nodes)
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
                    value = field.Value?.ToString(),
                });
            }

            list.Add(new
            {
                role = node.Role,
                key = node.Key,
                @class = node.Class,
                surface = node.Surface.ToString(),
                state = node.State.ToString(),
                actions = node.Actions,
                fields,
                children = ToNodes(node.Children),
            });
        }

        return list;
    }
}
