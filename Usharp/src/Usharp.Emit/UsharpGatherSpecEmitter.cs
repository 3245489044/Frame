using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次聚拢声明发射的产物：节点聚拢面 JSON + 发射期诊断。
/// <para>域缺失 / 形态非法 ⇒ **不产出**（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpGatherSpecResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no gather-spec diagnostics)");
}

/// <summary>
/// 节点聚拢声明 → 中立 JSON（<c>domain / kindField / groupByField? / families / kind-shaping</c>）。
/// <para>
/// 产出**逐字段含键序**等价于既有 <c>Config/nodes/node-gather-*.json</c>（等价门 = 两侧解析后按有序属性递归比较；
/// 序列化用 <see cref="JsonObject"/> 保插入序：族序 = 声明序，塑形字段键序 = 声明序）。
/// 耦合方向：本发射器**不引** <c>Modules.Node</c>（聚拢类型只在适配工程出现），故产出为中立 JSON。
/// </para>
/// <para>
/// 缺失该域 id 的 <c>[Gather]</c> ⇒ <c>US5009</c> 且不产出（不静默补内容）。
/// </para>
/// </summary>
public static class UsharpGatherSpecEmitter
{
    /// <summary>发射指定域的聚拢声明 JSON（域缺失 ⇒ 不产出 + <c>US5009</c>）。</summary>
    public static UsharpGatherSpecResult Emit(UsharpCompilation compilation, string domainId)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (string.IsNullOrWhiteSpace(domainId))
        {
            throw new ArgumentException("域 id 不得为空", nameof(domainId));
        }

        var diagnostics = new List<UsharpDiagnostic>();
        var gather = compilation.Gathers.FirstOrDefault(
            g => string.Equals(g.DomainId, domainId, StringComparison.Ordinal));

        if (gather is null)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.GatherSpecNotProducible,
                $"节点聚拢声明不可产出：未找到 [Gather] {domainId}（收集端口的事实唯一真源）",
                "(compilation)", 0));
            return new UsharpGatherSpecResult(null, diagnostics);
        }

        return new UsharpGatherSpecResult(Serialize(gather), diagnostics);
    }

    /// <summary>规范化 JSON（<see cref="JsonObject"/> 保插入序 ⇒ 键序 = 声明序，等价门可比对）。</summary>
    private static string Serialize(UsharpGatherDecl gather)
    {
        var root = new JsonObject
        {
            ["domain"] = gather.DomainId,
            ["kindField"] = gather.KindField,
        };

        // groupByField 仅在声明时产出（相当部分域无二级分组轴）。
        if (!string.IsNullOrWhiteSpace(gather.GroupBy))
        {
            root["groupByField"] = gather.GroupBy;
        }

        var families = new JsonObject();
        foreach (var name in gather.FamilyOrder)
        {
            var kinds = new JsonArray();
            foreach (var kind in gather.Families[name])
            {
                kinds.Add(kind);
            }
            families[name] = kinds;
        }
        root["families"] = families;

        var shapings = new JsonObject();
        foreach (var shaping in gather.Shapings)
        {
            var entry = new JsonObject();
            foreach (var field in shaping.Fields)
            {
                switch (field.Key)
                {
                    case UsharpLanguage.FieldSummary:
                        var summary = new JsonArray();
                        foreach (var item in shaping.SummaryList)
                        {
                            summary.Add(item);
                        }
                        entry[field.Key] = summary;
                        break;
                    case UsharpLanguage.FieldShaped:
                        if (bool.TryParse(field.Value, out var flag)) entry[field.Key] = flag;
                        else entry[field.Key] = field.Value;
                        break;
                    default:
                        entry[field.Key] = field.Value;
                        break;
                }
            }
            shapings[shaping.Kind] = entry;
        }
        root["kind-shaping"] = shapings;

        return root.ToJsonString();
    }
}
