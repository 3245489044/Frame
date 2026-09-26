using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次归属边声明发射的产物：<c>edge=</c> 边类型集 + 逐条 <c>[Ref]</c> 规范化 JSON + 发射期诊断。
/// <para>无任何带 <c>edge=</c> 的 <c>[Ref]</c> ⇒ **不产出**（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpRefEdgeResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no ref-edge diagnostics)");
}

/// <summary>
/// 归属边声明 → <c>edge=</c> 边类型集 + 引用明细（W1：归属事实的唯一声明面）。
/// <para>
/// 输入形态（既有 <c>[Ref]</c> 语法，零新语言单元）：
/// <c>[Ref] &lt;名&gt;: &lt;SpaceId&gt;:&lt;Key&gt; | edge=&lt;边类型&gt; | src=&lt;实体&gt; | tgt=&lt;实体&gt; | intent=…</c>。
/// <b>禁 <c>loc=</c></b> —— 该字段未注册且被编译器静默忽略（实测 0 错 0 警告），书写即假声明；
/// 边到位置的挂载由 <c>src</c>/<c>tgt</c> 承载。
/// </para>
/// <para>
/// 产出**中立 JSON**（BCL 文本，无领域类型）：<c>{"edgeTypes":[…],"refs":[{"name","space","key","src","tgt","intent","edge","scope"}]}</c> ——
/// 运行期（<c>ReferenceGrounding</c> 白名单）与门禁（<c>Check-NodeHierarchyRefs</c>）经各自端口/读数消费，
/// 本发射器不引位置层或存储层类型（耦合方向：禁 <c>Modules.Node → Usharp</c> / 禁 <c>DataStoreDriven → Usharp</c>）。
/// </para>
/// <para>
/// **命名归一不做**（与 <see cref="UsharpLocPlacementEmitter"/> 的唯一差异）：边类型值是**不透明运行期取值**
/// （与 <c>CrossDbReferenceTypes</c> 常量逐字对账，如 <c>history_owns_task</c>），下划线形即真值，
/// 归一为 kebab 形会破坏双向对账 ⇒ 本发射器原样透出，禁任一侧再归一。
/// </para>
/// </summary>
/// <remarks>
/// 命名沿革：《节点域全量工作流化（声明驱动）总体方案》§5 W1 记为 <c>UsharpRefLocEmitter</c>；
/// 因 W1 已裁「禁 <c>loc=</c>」（R3：未注册字段被静默忽略 ⇒ 假声明），实现改名以名实相符。
/// </remarks>
public static class UsharpRefEdgeEmitter
{
    /// <summary>`[Ref]` 的 `edge=` 字段名（唯一权威；门禁 E3 字段白名单与之一致）。</summary>
    public const string EdgeField = UsharpLanguage.FieldEdge;

    /// <summary>发射本编译的归属边声明（无带 <c>edge=</c> 的 <c>[Ref]</c> ⇒ 不产出 + `US5007`）。</summary>
    public static UsharpRefEdgeResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var declared = compilation.Refs
            .Where(r => !string.IsNullOrWhiteSpace(r.Edge))
            .ToList();

        if (declared.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.RefEdgeNotProducible,
                $"归属边声明不可产出：本编译无任何带 {EdgeField}= 的 [Ref]（归属事实的唯一声明面缺失）",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 0));
            return new UsharpRefEdgeResult(null, diagnostics);
        }

        // 边类型集（声明序去重；**原样透出**，见类注释「命名归一不做」）
        var edgeTypes = new List<string>();
        foreach (var reference in declared)
        {
            var edge = reference.Edge!.Trim();
            if (!edgeTypes.Contains(edge, StringComparer.Ordinal))
            {
                edgeTypes.Add(edge);
            }
        }

        return new UsharpRefEdgeResult(Serialize(edgeTypes, declared), diagnostics);
    }

    /// <summary>规范化 JSON（<c>JsonArray</c>/<c>JsonObject</c> 保插入序 ⇒ 序 = 声明序，等价门可比对）。</summary>
    private static string Serialize(IReadOnlyList<string> edgeTypes, IReadOnlyList<UsharpRefDecl> refs)
    {
        var edgeArray = new JsonArray();
        foreach (var edgeType in edgeTypes)
        {
            edgeArray.Add(edgeType);
        }

        var refArray = new JsonArray();
        foreach (var reference in refs)
        {
            refArray.Add(new JsonObject
            {
                ["name"] = reference.Name,
                ["space"] = reference.SpaceId,
                ["key"] = reference.Key,
                ["src"] = reference.Source,
                ["tgt"] = reference.Target,
                ["intent"] = reference.Intent,
                ["edge"] = reference.Edge!.Trim(),
                ["scope"] = reference.Scope,
            });
        }

        var root = new JsonObject
        {
            ["edgeTypes"] = edgeArray,
            ["refs"] = refArray,
        };

        return root.ToJsonString();
    }
}
