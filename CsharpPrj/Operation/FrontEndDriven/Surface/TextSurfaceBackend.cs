#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.Surface;

/// <summary>纯文本后端投影器（CLI/日志）：缩进/前缀/分隔符取自载体映射声明。</summary>
public sealed class TextSurfaceBackend : IBackendProjector<string>
{
    private readonly SurfaceMapDeclaration _map;

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键）。</param>
    public TextSurfaceBackend(SurfaceMapDeclaration map, string backendId = "text")
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        BackendId = backendId;
    }

    /// <inheritdoc />
    public string BackendId { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> UnsupportedCells => Array.Empty<string>();

    /// <inheritdoc />
    public string Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var builder = new StringBuilder();
        foreach (var node in plan.Roots) Render(node, 0, builder);
        return builder.ToString();
    }

    private void Render(UiSurfaceNode node, int depth, StringBuilder builder)
    {
        var props = _map.Props(BackendId, node.Role);
        var prefix = Props(props, "prefix");
        builder.Append(' ', depth * 2).Append(prefix).Append(node.Class);

        foreach (var field in node.Fields)
        {
            var fieldProps = _map.Props(BackendId, field.Cell);
            var sep = Props(fieldProps, "sep");
            if (sep.Length == 0) sep = " ";
            builder.Append(' ').Append(field.Name).Append(sep).Append(TextOf(field));
        }

        builder.AppendLine();
        foreach (var child in node.Children) Render(child, depth + 1, builder);
    }

    private static string Props(IReadOnlyDictionary<string, string>? props, string name)
        => props is not null && props.TryGetValue(name, out var value) ? value : string.Empty;

    private static string TextOf(UiField field)
        => field.IsUnknown || field.Value is null
            ? "?"
            : field.Value is string text ? text : field.Value.ToString() ?? "?";
}
