using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次域定义发射的产物：节点域定义面 JSON + 发射期诊断。
/// <para>域缺失 / 形态非法 ⇒ **不产出**（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpNodeSpecResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 节点域定义面声明 → 中立 JSON（<c>domain / identity / context / sequence / placements / selections / retentions</c>）。
/// <para>
/// 产出**逐字段含键序**等价于既有 <c>Config/nodes/*.json</c>（等价门 = 两侧解析后按有序属性递归比较；
/// 序列化用 <see cref="JsonObject"/> 保插入序）。耦合方向：本发射器**不引** <c>Modules.Node</c>
/// （节点类型只在适配工程 <c>Usharp.Adapters.Node</c> 出现），故产出为中立 JSON。
/// </para>
/// <para>
/// 缺失该域 id 的 <c>[Domain]</c> ⇒ <c>US5008</c> 且不产出（不静默补内容）。
/// </para>
/// </summary>
public static class UsharpNodeSpecEmitter
{
    /// <summary>发射指定域的节点域定义面 JSON（域缺失 ⇒ 不产出 + <c>US5008</c>）。</summary>
    public static UsharpNodeSpecResult Emit(UsharpCompilation compilation, string domainId)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (string.IsNullOrWhiteSpace(domainId))
        {
            throw new ArgumentException("域 id 不得为空", nameof(domainId));
        }

        var found = UsharpEmit.Document(
            compilation.Domains,
            domainId,
            domain => domain.DomainId,
            UsharpCodes.DomainSpecNotProducible,
            $"节点域定义面不可产出：未找到 [Domain] {domainId}（域定义面的事实唯一真源）",
            "(compilation)",
            Serialize);

        return new UsharpNodeSpecResult(found.Value, found.Diagnostics);
    }

    /// <summary>规范化 JSON（<see cref="JsonObject"/> 保插入序 ⇒ 序 = 声明序，等价门可比对）。</summary>
    private static string Serialize(UsharpDomainDecl domain)
    {
        var root = new JsonObject
        {
            ["domain"] = domain.DomainId,
            ["identity"] = domain.Identity,
            ["context"] = StringArray(domain.Context),
        };

        // sequence 仅在声明时产出（archive / session-view 等单键域无 sequence）。
        if (domain.Sequence.Count > 0)
        {
            root["sequence"] = StringArray(domain.Sequence);
        }

        var placements = new JsonArray();
        foreach (var placement in domain.Placements)
        {
            var item = new JsonObject
            {
                ["id"] = placement.Id,
                ["level"] = placement.Level,
                ["carrier"] = placement.Carrier,
                ["key"] = placement.Key,
                ["form"] = placement.Form,
                ["retention"] = placement.Retention,
                ["selection"] = placement.Selection,
                ["writer"] = placement.Writer,
            };
            if (!string.IsNullOrWhiteSpace(placement.State))
            {
                item["state"] = placement.State;
            }
            placements.Add(item);
        }
        root["placements"] = placements;
        root["selections"] = Map(domain.Selections);
        root["retentions"] = Map(domain.Retentions);
        if (domain.Marks.Count > 0)
        {
            root["marks"] = Map(domain.Marks);
        }

        if (domain.Tags.Count > 0)
        {
            root["tags"] = Map(domain.Tags);
        }

        if (domain.Behaviors.Count > 0)
        {
            root["behaviors"] = Map(domain.Behaviors);
        }

        if (domain.Origins.Count > 0)
        {
            root["origins"] = StringArray(domain.Origins);
        }

        if (domain.Coordinates.Count > 0)
        {
            var coordinates = new JsonArray();
            foreach (var coordinate in domain.Coordinates)
            {
                coordinates.Add(new JsonObject
                {
                    ["name"] = coordinate.Name,
                    ["family"] = coordinate.Family,
                    ["axis"] = coordinate.Axis,
                    ["canonical"] = coordinate.Canonical,
                });
            }
            root["coordinates"] = coordinates;
        }

        return root.ToJsonString();
    }

    private static JsonArray StringArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }
        return array;
    }

    private static JsonObject Map(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (var pair in values)
        {
            obj[pair.Key] = pair.Value;
        }
        return obj;
    }
}
