#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>字段词表声明发射产物（field-vocabulary-v1 + 诊断）。</summary>
public sealed record UsharpFieldVocabularyResult(
    string? Json, IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>[Record] ftype_* / fdef_* → {schema, types, fields}（纯投影）。</summary>
public static class UsharpFieldVocabularyEmitter
{
    public const string Schema = "field-vocabulary-v1";
    public const string TypeRecordPrefix = "ftype_";
    public const string FieldRecordPrefix = "fdef_";

    public static UsharpFieldVocabularyResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var diagnostics = new List<UsharpDiagnostic>();
        var types = new JsonObject();
        var fields = new JsonObject();

        foreach (var record in compilation.Records)
        {
            if (record.RecordId.StartsWith(TypeRecordPrefix, StringComparison.Ordinal))
            {
                var type = UsharpRecordJson.Str(record.Body, "type", record.RecordId[TypeRecordPrefix.Length..]);
                types[type] = new JsonObject
                {
                    ["cell"] = UsharpRecordJson.Str(record.Body, "cell", string.Empty),
                    ["shape"] = UsharpRecordJson.Str(record.Body, "shape", "text"),
                    ["caps"] = UsharpRecordJson.Array(UsharpRecordJson.StrList(record.Body, "caps")),
                };
            }
            else if (record.RecordId.StartsWith(FieldRecordPrefix, StringComparison.Ordinal))
            {
                var field = UsharpRecordJson.Str(record.Body, "field", record.RecordId[FieldRecordPrefix.Length..]);
                var node = new JsonObject
                {
                    ["type"] = UsharpRecordJson.Str(record.Body, "type", "unknown"),
                };
                var label = UsharpRecordJson.StrOrNull(record.Body, "labelKey");
                if (label is not null) node["labelKey"] = label;
                fields[field] = node;
            }
        }

        if (types.Count == 0 || fields.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                "字段词表不可产出：缺 [Record] ftype_* 或 fdef_*",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1));
            return new UsharpFieldVocabularyResult(null, diagnostics);
        }

        return new UsharpFieldVocabularyResult(
            new JsonObject { ["schema"] = Schema, ["types"] = types, ["fields"] = fields }.ToJsonString(),
            diagnostics);
    }
}
