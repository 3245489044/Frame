#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>载体映射声明发射产物（surface-map-v1 + 诊断）。</summary>
public sealed record UsharpSurfaceMapResult(
    string? Json, IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 载体映射**投影器**：唯一结构源 = <c>ui_part_*</c>（同一 ui 闭包），<c>surface-map</c> 降为派生产物。
/// <para>
/// 投影规则：key = part.name（cell 记录剥 <c>cell_</c> 前缀，class 记录不参与结构映射）；
/// html 的 <c>element</c> 重命名为 <c>tag</c>（消费方读取键）；<c>__default</c> 取自 <c>ui_part_unknown</c>。
/// </para>
/// </summary>
public static class UsharpSurfaceMapEmitter
{
    public const string Schema = "surface-map-v1";
    public const string PartPrefix = "ui_part_";
    public const string CellPrefix = "cell_";
    public const string DefaultKey = "__default";
    public const string UnknownPart = "unknown";

    public static UsharpSurfaceMapResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var diagnostics = new List<UsharpDiagnostic>();
        var roles = new JsonObject();

        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(PartPrefix, StringComparison.Ordinal)) continue;
            var kind = UsharpRecordJson.Str(record.Body, "kind", "role");
            if (string.Equals(kind, "class", StringComparison.Ordinal)) continue;

            var name = UsharpRecordJson.Str(record.Body, "name", record.RecordId[PartPrefix.Length..]);
            var key = name.StartsWith(CellPrefix, StringComparison.Ordinal) ? name[CellPrefix.Length..] : name;
            roles[key] = Carriers(record.Body);
        }

        if (roles.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                "载体映射不可产出：缺 [Record] ui_part_*（结构源单点）",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1));
            return new UsharpSurfaceMapResult(null, diagnostics);
        }

        if (!roles.ContainsKey(DefaultKey))
        {
            if (roles.TryGetPropertyValue(UnknownPart, out var unknown) && unknown is not null)
            {
                roles[DefaultKey] = unknown.DeepClone();
            }
            else
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                    "载体映射不可产出：缺 ui_part_unknown（__default 兜底来源）",
                    compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1));
                return new UsharpSurfaceMapResult(null, diagnostics);
            }
        }

        return new UsharpSurfaceMapResult(
            new JsonObject { ["schema"] = Schema, ["roles"] = roles }.ToJsonString(), diagnostics);
    }

    /// <summary>part.carriers → { 载体 id: { 属性: 值 } }（element → tag 键名归一）。</summary>
    private static JsonObject Carriers(UsharpRecord record)
    {
        var node = new JsonObject();
        if (!record.TryGet("carriers", out var value) || value is not UsharpRecord carriers) return node;

        foreach (var pair in carriers.Fields)
        {
            if (pair.Value is not UsharpRecord carrier) continue;
            node[pair.Key] = Props(carrier);
        }

        return node;
    }

    private static JsonObject Props(UsharpRecord record)
    {
        var node = new JsonObject();
        foreach (var pair in record.Fields)
        {
            if (pair.Value is not UsharpScalar { Kind: not UsharpScalarKind.Null } scalar) continue;
            var key = string.Equals(pair.Key, "element", StringComparison.Ordinal) ? "tag" : pair.Key;
            node[key] = ScalarNode(scalar);
        }

        return node;
    }

    private static JsonNode? ScalarNode(UsharpScalar scalar)
        => scalar.Kind switch
        {
            UsharpScalarKind.Bool => JsonValue.Create(string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase)),
            UsharpScalarKind.Int when int.TryParse(scalar.Text, out var number) => JsonValue.Create(number),
            UsharpScalarKind.Null => null,
            _ => JsonValue.Create(scalar.Text),
        };
}
