#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>聚拢视图声明发射产物（gather-spec-v1 JSON + 诊断）。</summary>
public sealed record UsharpGatherViewResult(
    string? Json, IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 扁平记录 → gather-spec-v1（纯投影；V1–V15 由 GatherSpecLoader 判定）。
/// 记录族：gather_view_* / gather_stage_* / gather_need_* / gather_produce_*，用 view 字段归属。
/// </summary>
public static class UsharpGatherViewSpecEmitter
{
    public const string Schema = "gather-spec-v1";
    public const string Table = "GatherView";
    public const string ViewPrefix = "gather_view_";
    public const string StagePrefix = "gather_stage_";
    public const string NeedPrefix = "gather_need_";
    public const string ProducePrefix = "gather_produce_";

    public static UsharpGatherViewResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var diagnostics = new List<UsharpDiagnostic>();
        var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)";
        var order = new List<string>();
        var byView = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        JsonObject View(string key)
        {
            if (byView.TryGetValue(key, out var existing)) return existing;
            var created = new JsonObject
            {
                ["view"] = key,
                ["stages"] = new JsonArray(),
                ["needs"] = new JsonArray(),
                ["produces"] = new JsonArray(),
            };
            byView[key] = created;
            order.Add(key);
            return created;
        }

        foreach (var record in compilation.Records)
        {
            var id = record.RecordId;
            var body = record.Body;
            foreach (var line in record.UnparsedLines)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.GatherShapeInvalid,
                    $"[Record] {id}: 无法解析的字段行 '{line}'", record.SourceFile, record.LineNumber));
            }

            if (id.StartsWith(ViewPrefix, StringComparison.Ordinal))
            {
                var view = View(UsharpRecordJson.Str(body, "view", id[ViewPrefix.Length..]));
                var extendsValue = UsharpRecordJson.StrOrNull(body, "extends");
                if (extendsValue is not null) view["extends"] = extendsValue;
                AddList(view, "excludeStages", UsharpRecordJson.StrList(body, "excludeStages"));
                AddList(view, "excludeNeeds", UsharpRecordJson.StrList(body, "excludeNeeds"));
                AddList(view, "excludeProduces", UsharpRecordJson.StrList(body, "excludeProduces"));
            }
            else if (id.StartsWith(StagePrefix, StringComparison.Ordinal))
            {
                var view = View(UsharpRecordJson.Str(body, "view", string.Empty));
                ((JsonArray)view["stages"]!).Add(new JsonObject
                {
                    ["name"] = UsharpRecordJson.Str(body, "name", string.Empty),
                    ["needs"] = UsharpRecordJson.Array(UsharpRecordJson.StrList(body, "needs")),
                    ["deps"] = UsharpRecordJson.Array(UsharpRecordJson.StrList(body, "deps")),
                    ["sections"] = UsharpRecordJson.Array(UsharpRecordJson.StrList(body, "sections")),
                });
            }
            else if (id.StartsWith(NeedPrefix, StringComparison.Ordinal))
            {
                var view = View(UsharpRecordJson.Str(body, "view", string.Empty));
                var node = new JsonObject
                {
                    ["domain"] = UsharpRecordJson.Str(body, "domain", string.Empty),
                    ["scope"] = UsharpRecordJson.Str(body, "scope", string.Empty),
                };
                var cost = UsharpRecordJson.StrOrNull(body, "cost");
                if (cost is not null) node["cost"] = cost;
                if (UsharpRecordJson.Bool(body, "reusable", false)) node["reusable"] = true;
                ((JsonArray)view["needs"]!).Add(node);
            }
            else if (id.StartsWith(ProducePrefix, StringComparison.Ordinal))
            {
                var view = View(UsharpRecordJson.Str(body, "view", string.Empty));
                var node = new JsonObject
                {
                    ["name"] = UsharpRecordJson.Str(body, "name", string.Empty),
                    ["kind"] = UsharpRecordJson.Str(body, "kind", string.Empty),
                    ["retention"] = UsharpRecordJson.Str(body, "retention", "contextual"),
                    ["crit"] = UsharpRecordJson.Str(body, "crit", "micro"),
                };
                var recompute = UsharpRecordJson.StrOrNull(body, "recompute");
                if (recompute is not null) node["recompute"] = recompute;
                ((JsonArray)view["produces"]!).Add(node);
            }
        }

        if (order.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.GatherSpecNotProducible,
                "聚拢视图声明不可产出：未找到任何 [Record] gather_view_*", file, 1));
            return new UsharpGatherViewResult(null, diagnostics);
        }

        if (diagnostics.Any(d => d.Severity == UsharpSeverity.Error))
        {
            return new UsharpGatherViewResult(null, diagnostics);
        }

        var views = new JsonObject();
        foreach (var key in order) views[key] = byView[key];
        return new UsharpGatherViewResult(
            new JsonObject { ["schema"] = Schema, ["views"] = views }.ToJsonString(), diagnostics);
    }

    private static void AddList(JsonObject view, string name, IReadOnlyList<string> values)
    {
        if (values.Count > 0) view[name] = UsharpRecordJson.Array(values);
    }
}
