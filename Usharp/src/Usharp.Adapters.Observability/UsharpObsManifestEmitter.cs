using System.Text;
using System.Text.Json;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Observability;

/// <summary>
/// <c>[Obs]</c> → 确定性 manifest 产出（读写同源：格式 = 内核 <c>ObservabilitySemanticsJson</c> 的加载格式，
/// 亦即 <c>Frame\DataCenter\schemas\events.schema.json</c> 结构门）。
/// <para>
/// 确定性：**保留声明序**（<c>entities</c> = 实体声明序；<c>events</c> = 事件声明序），缩进 2 空格、<c>\n</c> 行尾，
/// **不做字母排序**（排序会破坏与既有台账的规范化全等）。事件项只产
/// <c>entity</c>/<c>required</c>/<c>enum</c>/<c>metrics</c>（空项省略），字段序与既有台账一致（位序即语义）。
/// </para>
/// <para>编译期不写盘、不落库（H3）：本件只返回文本，写盘/落库由宿主显式驱动。</para>
/// </summary>
public static class UsharpObsManifestEmitter
{
    /// <summary>产出 manifest 文本（含末尾换行；空声明 → 仅 <c>{"events":{}}</c>）。</summary>
    public static string Render(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            if (!string.IsNullOrWhiteSpace(compilation.Obs.FileVersion))
                writer.WriteString("version", compilation.Obs.FileVersion);

            writer.WriteStartArray("entities");
            foreach (var entity in compilation.Obs.Entities) writer.WriteStringValue(entity.Name);
            writer.WriteEndArray();

            writer.WriteStartObject("events");
            foreach (var @event in compilation.Obs.Events) WriteEvent(writer, @event);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>单条事件项（字段序 = 既有台账：entity → required → enum → metrics；空项省略）。</summary>
    private static void WriteEvent(Utf8JsonWriter writer, UsharpObsDecl decl)
    {
        writer.WriteStartObject(decl.Name);

        if (!string.IsNullOrWhiteSpace(decl.Entity)) writer.WriteString("entity", decl.Entity);
        WriteStringArray(writer, "required", decl.Required);

        if (decl.Enums.Count > 0)
        {
            writer.WriteStartObject("enum");
            foreach (var (field, allowed) in decl.Enums)
            {
                writer.WriteStartArray(field);
                foreach (var value in allowed) writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        WriteStringArray(writer, "metrics", decl.Metrics);

        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;

        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }
}
