using System.Text.Json.Nodes;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>一次产品图发射的产物：<c>product.graph.generated.json</c> + 发射期诊断。</summary>
public sealed record UsharpProductGraphResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 产品图声明面（<c>[Product]</c> / <c>[Face]</c>）→ <c>product.graph.generated.json</c>（levels / products / faces）。
/// <para>
/// <b>中性 JSON</b>：本发射器不依赖 <c>DataCenter.Protocol</c>（DeclaredGraph 的宿主侧映射由适配器承担，
/// 对齐 <see cref="UsharpRelationPlanEmitter"/> 的「发射器只出声明事实」纪律）。
/// 无任何声明 ⇒ 不产出 + <c>US8310</c>（可见化，不静默给空图）。
/// </para>
/// <para>保声明序（产品 / 面），字段数组保声明序 ⇒ 等价门可逐字段比对。</para>
/// </summary>
public static class UsharpProductGraphEmitter
{
    /// <summary>发射本编译的产品图（无声明 ⇒ <c>null</c> + <c>US8310</c>）。</summary>
    public static UsharpProductGraphResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        if (compilation.Products.Count == 0 && compilation.Faces.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.ProductGraphNotProducible,
                "产品图不可产出：本编译无任何 [Product]/[Face] 声明",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 0));
            return new UsharpProductGraphResult(null, diagnostics);
        }

        return new UsharpProductGraphResult(Serialize(compilation), diagnostics);
    }

    private static string Serialize(UsharpCompilation compilation)
    {
        var levels = new JsonArray();
        var seenLevels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var space in compilation.Spaces.Spaces)
        {
            if (!seenLevels.Add(space.SpaceId)) continue;
            levels.Add(new JsonObject
            {
                ["id"] = space.SpaceId,
                ["entityType"] = space.Kind.ToString().ToLowerInvariant(),
            });
        }

        var products = new JsonArray();
        foreach (var product in compilation.Products)
        {
            products.Add(new JsonObject
            {
                ["id"] = product.ProductId,
                ["level"] = product.LevelId,
                ["role"] = product.Role,
                ["in"] = UsharpJson.Array(product.InFields),
                ["out"] = UsharpJson.Array(product.OutFields),
                ["idempotency"] = product.IdempotencyKey,
                ["version"] = product.Version,
                ["stage"] = product.Stage,
                ["dependsOn"] = UsharpJson.Array(product.DependsOn),
                ["accepts"] = UsharpJson.Array(product.Accepts),
                ["derive"] = product.DeriveId,
                ["derives"] = product.Derives,
            });
        }

        var faces = new JsonArray();
        foreach (var face in compilation.Faces)
        {
            faces.Add(new JsonObject
            {
                ["id"] = face.FaceId,
                ["product"] = face.ProductId,
                ["owners"] = UsharpJson.Array(face.Owners),
                ["channels"] = UsharpJson.Array(face.Channels),
                ["fields"] = face.FieldsDeclared ? UsharpJson.Array(face.Fields) : null,
            });
        }

        var root = new JsonObject
        {
            ["version"] = "1.0",
            ["levels"] = levels,
            ["products"] = products,
            ["faces"] = faces,
        };

        return root.ToJsonString();
    }
}