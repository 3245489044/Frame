using System.Text.Json.Nodes;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>一次读模型发射的产物：<c>readmodel.spec.generated.json</c> + 发射期诊断。</summary>
public sealed record UsharpReadModelResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 读模型声明面（<c>[ReadModel]</c>）→ <c>readmodel.spec.generated.json</c>（模型 id → 源键/版本源/投影器/参数/字段）。
/// <para>
/// 无声明 ⇒ 产出空表（<c>readModels: []</c>），不报错（读模型面可选；死声明由闸A <c>US7708</c> 负责）。
/// 保声明序与字段序 ⇒ 等价门可逐字段比对。
/// </para>
/// </summary>
public static class UsharpReadModelEmitter
{
    /// <summary>发射本编译的读模型规格（无声明 ⇒ 空表 JSON）。</summary>
    public static UsharpReadModelResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return new UsharpReadModelResult(Serialize(compilation), Array.Empty<UsharpDiagnostic>());
    }

    private static string Serialize(UsharpCompilation compilation)
    {
        var readModels = new JsonArray();
        foreach (var model in compilation.ReadModels)
        {
            var item = new JsonObject
            {
                ["id"] = model.ModelId,
                ["sources"] = UsharpJson.Array(model.Sources),
                ["version"] = model.Version,
                ["projector"] = model.Projector,
                ["args"] = UsharpJson.Array(model.Args),
            };

            if (model.TtlMs is { } ttl) item["ttl"] = ttl;
            if (model.FieldsDeclared) item["fields"] = UsharpJson.Array(model.Fields);

            readModels.Add(item);
        }

        var root = new JsonObject
        {
            ["version"] = "1.0",
            ["readModels"] = readModels,
        };

        return root.ToJsonString();
    }
}