#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.Surface;

/// <summary>
/// 纯文本后端投影器（CLI/日志）：缩进/前缀/分隔符取自同一份 UI 声明（结构映射 + 声明的结构属性）。
/// <para>视觉槽位在该载体不可承载 ⇒ 记入 <see cref="UnsupportedCells"/>（可见降级，禁静默）。</para>
/// </summary>
public sealed class TextSurfaceBackend : IBackendProjector<string>
{
    private readonly SurfaceMapDeclaration _map;
    private readonly UiDeclaration? _declaration;
    private readonly string? _skin;
    private readonly HashSet<UnsupportedSlot> _unsupported = new();

    /// <param name="map">载体映射声明。</param>
    /// <param name="backendId">后端 id（声明面键）。</param>
    /// <param name="declaration">UI 个性化声明（可空 ⇒ 仅结构映射，零行为变化）。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明默认档）。</param>
    public TextSurfaceBackend(
        SurfaceMapDeclaration map,
        string backendId = "text",
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
    public IReadOnlyList<UnsupportedSlot> UnsupportedCells => _unsupported.ToList();

    /// <inheritdoc />
    public string Project(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var builder = new StringBuilder();
        var theme = _declaration?.Resolve(_skin);
        foreach (var node in plan.Roots) Render(node, 0, builder, theme);
        return builder.ToString();
    }

    private void Render(UiSurfaceNode node, int depth, StringBuilder builder, ResolvedUi? theme)
    {
        var props = _map.Props(BackendId, node.Role);
        var part = theme?.ThemeFor(node.Role, node.Class);
        var prefix = Props(props, "prefix");
        if (prefix.Length == 0) prefix = Structure(part, "prefix");
        builder.Append(' ', depth * 2).Append(prefix).Append(node.Class);

        foreach (var field in node.Fields)
        {
            var fieldProps = _map.Props(BackendId, field.Cell);
            var fieldPart = theme?.ThemeForCell(field.Cell);
            var sep = Props(fieldProps, "sep");
            if (sep.Length == 0) sep = Structure(fieldPart, "sep");
            if (sep.Length == 0) sep = " ";
            builder.Append(' ').Append(field.Name).Append(sep).Append(TextOf(field));
            TrackUnsupported($"{field.Cell}:{field.Name}", fieldPart);
        }

        builder.AppendLine();
        TrackUnsupported(node.Role, part);
        foreach (var child in node.Children) Render(child, depth + 1, builder, theme);
    }

    /// <summary>声明结构属性（角色部件的 carriers 记录）：无声明 ⇒ 空串（回落结构映射）。</summary>
    private string Structure(UiTheme? part, string name)
        => part?.Structure(BackendId) is { } structure && structure.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    /// <summary>记录该载体不承载的语义槽位（可见降级，不静默丢）。</summary>
    private void TrackUnsupported(string key, UiTheme? part)
    {
        if (part is null) return;
        var name = part.Name ?? string.Empty;
        foreach (var slot in part.MissingSlots(BackendId))
        {
            _unsupported.Add(new UnsupportedSlot(BackendId, name, slot, "token-missing"));
        }
    }

    private static string Props(IReadOnlyDictionary<string, string>? props, string name)
        => props is not null && props.TryGetValue(name, out var value) ? value : string.Empty;

    private static string TextOf(UiField field) => FieldTextFormatter.Format(field);
}
