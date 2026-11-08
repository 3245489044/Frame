#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

/// <summary>字段类型规格（type → 渲染单元 + 值形态 + 能力）。</summary>
/// <param name="Type">类型名。</param>
/// <param name="Cell">渲染单元。</param>
/// <param name="Shape">值形态受控词（structured | text）。</param>
/// <param name="Caps">能力集（受控词）。</param>
public sealed record FieldTypeSpec(string Type, string Cell, string Shape, IReadOnlyList<string> Caps);

/// <summary>字段规格（field → 类型 + 标签键）。</summary>
/// <param name="Name">字段名。</param>
/// <param name="Type">类型名。</param>
/// <param name="Cell">渲染单元（由类型解析）。</param>
/// <param name="Shape">值形态受控词（由类型解析）。</param>
/// <param name="Caps">能力集。</param>
/// <param name="LabelKey">标签 loc 键（可空）。</param>
public sealed record FieldSpec(
    string Name, string Type, string Cell, string Shape, IReadOnlyList<string> Caps, string? LabelKey = null);

/// <summary>
/// 字段词表声明（生成物 <c>fields.generated.json</c>；真源 = <c>Config\nodes\fields.ushar</c>）。
/// <para>未登记字段 ⇒ 中性规格（单元为 unknown 仅供样式；语义三态由 D 层字段口径裁决，禁由 Cell 推断）。</para>
/// </summary>
public sealed class FieldDeclaration
{
    /// <summary>值形态：需要结构值（对象/数组）而非纯文本。</summary>
    public const string StructuredShape = "structured";

    /// <summary>值形态：纯文本。</summary>
    public const string TextShape = "text";

    /// <summary>空声明（未装配/不可用）：一切字段按 unknown 呈现（可见降级）。</summary>
    public static readonly FieldDeclaration Empty = new(
        new Dictionary<string, FieldTypeSpec>(StringComparer.Ordinal),
        new Dictionary<string, FieldSpec>(StringComparer.Ordinal),
        declared: false);

    private readonly IReadOnlyDictionary<string, FieldTypeSpec> _types;
    private readonly IReadOnlyDictionary<string, FieldSpec> _fields;

    private FieldDeclaration(
        IReadOnlyDictionary<string, FieldTypeSpec> types,
        IReadOnlyDictionary<string, FieldSpec> fields,
        bool declared)
    {
        _types = types;
        _fields = fields;
        IsDeclared = declared;
    }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>已声明字段名集（覆盖门禁比对用）。</summary>
    public IReadOnlyCollection<string> FieldNames => (IReadOnlyCollection<string>)_fields.Keys;

    /// <summary>解析字段规格（未声明 ⇒ 中性文本单元，不伪造 unknown 语义）。</summary>
    /// <param name="name">字段名。</param>
    public FieldSpec Resolve(string name)
    {
        if (name is not null && _fields.TryGetValue(name, out var spec)) return spec;
        return new FieldSpec(
            name ?? string.Empty, "undeclared", SurfaceCellIds.Unknown, TextShape, Array.Empty<string>());
    }

    /// <summary>是否登记过该字段（声明面事实；门禁/合成共用）。</summary>
    /// <param name="name">字段名。</param>
    public bool Declares(string name) => name is not null && _fields.ContainsKey(name);

    /// <summary>解析字段规格（未登记 ⇒ false，调用方按声明面裁决）。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="spec">字段规格。</param>
    public bool TryResolve(string name, out FieldSpec spec)
    {
        if (name is not null && _fields.TryGetValue(name, out var found))
        {
            spec = found;
            return true;
        }

        spec = Resolve(name);
        return false;
    }

    /// <summary>解析声明 JSON（不可解析 ⇒ 抛：失败姿态由调用方决定）。</summary>
    /// <param name="json">声明文本。</param>
    public static FieldDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = ConversationJson.Root(json);

        var types = new Dictionary<string, FieldTypeSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("types", out var typesElement) && typesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in typesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                var id = property.Name;
                var cell = ReadText(property.Value, "cell") ?? SurfaceCellIds.Unknown;
                var shape = ReadText(property.Value, "shape") ?? TextShape;
                types[id] = new FieldTypeSpec(
                    id, SurfaceCellIds.Normalize(cell), shape, ReadList(property.Value, "caps"));
            }
        }

        var fields = new Dictionary<string, FieldSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in fieldsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                var name = property.Name;
                var type = ReadText(property.Value, "type") ?? "unknown";
                var typeSpec = types.TryGetValue(type, out var found)
                    ? found
                    : new FieldTypeSpec(type, SurfaceCellIds.Unknown, TextShape, Array.Empty<string>());
                fields[name] = new FieldSpec(
                    name, type, typeSpec.Cell, typeSpec.Shape, typeSpec.Caps, ReadText(property.Value, "labelKey"));
            }
        }

        return new FieldDeclaration(types, fields, declared: fields.Count > 0);
    }

    private static string? ReadText(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> ReadList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text) list.Add(text);
        }

        return list;
    }
}
