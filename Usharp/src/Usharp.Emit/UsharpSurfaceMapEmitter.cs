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
/// [Record] smap_*（[Table] SurfaceMap）→ {schema, roles}：每个记录 = 一个 role/cell 键，
/// 其嵌套记录字段名即后端 id（声明驱动，发射器不含任何载体名）。
/// </summary>
public static class UsharpSurfaceMapEmitter
{
    public const string Schema = "surface-map-v1";
    public const string RecordPrefix = "smap_";

    public static UsharpSurfaceMapResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var diagnostics = new List<UsharpDiagnostic>();
        var roles = new JsonObject();

        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(RecordPrefix, StringComparison.Ordinal)) continue;

            var key = UsharpRecordJson.Str(record.Body, "key", record.RecordId[RecordPrefix.Length..]);
            var entry = new JsonObject();
            foreach (var pair in record.Body.Fields)
            {
                if (string.Equals(pair.Key, "key", StringComparison.Ordinal)) continue;
                if (pair.Value is UsharpRecord carrier)
                {
                    entry[pair.Key] = UsharpRecordJson.Props(carrier);
                }
            }

            roles[key] = entry;
        }

        if (roles.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                "载体映射不可产出：未找到任何 [Record] smap_*",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1));
            return new UsharpSurfaceMapResult(null, diagnostics);
        }

        return new UsharpSurfaceMapResult(
            new JsonObject { ["schema"] = Schema, ["roles"] = roles }.ToJsonString(), diagnostics);
    }
}
