using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using RefModels = UniversalWorkflowFramework.Reference.Models;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 引用关系唯一宿主的投影适配器（读侧）。
/// 遵循铁律：一切引用关系统一落到 UniversalWorkflowFramework 的 IUniversalReferenceManager（权威宿主），
/// 再"投影"为本框架的 Owns/Calls/Depends/Provides/Consumes 符号，落到电路图上。
/// 适配层读宿主、产出符号；不反向修改业务维护代码（吸取 IUniversalReferenceManager 空转教训）。
/// </summary>
public sealed class UniversalReferenceProjector
{
    private readonly IUniversalReferenceManager _manager;

    public UniversalReferenceProjector(IUniversalReferenceManager manager) => _manager = manager;

    /// <summary>把宿主的引用关系投影为引用符号（追加到 circuit.Symbols），返回新增符号数。</summary>
    public async Task<int> ProjectIntoAsync(Circuit circuit, CancellationToken cancellationToken = default)
    {
        var resources = await _manager.GetAllResourcesAsync();
        var added = 0;
        var seen = new HashSet<string>();

        foreach (var resource in resources)
        {
            var tuples = await _manager.GetReferencesAsync(resource.Identifier, includeIncoming: false);
            foreach (var (src, tgt, type, _, description, _, _) in tuples)
            {
                if (!MapRelation(type, out var relation)) continue;
                var key = $"{relation}|{src.GetUniqueKey()}|{tgt.GetUniqueKey()}";
                if (!seen.Add(key)) continue;

                circuit.Append(new ReferenceSymbol
                {
                    Relation = relation,
                    Source = src.GetUniqueKey(),
                    Target = tgt.GetUniqueKey(),
                    EdgeLiteral = type.ToString(),
                });
                added++;
            }
        }
        return added;
    }

    /// <summary>把宿主引用类型枚举映射为本框架的引用关系语义。</summary>
    /// <remarks>历史兼容映射（D1）：宿主 <see cref="RefModels.ReferenceType"/> 为 6 值封闭枚举，
    /// 语义源 = <c>ReferenceEdgeTypes</c> / <see cref="ReferenceSymbol"/>.<see cref="ReferenceRelation"/>；
    /// 本映射仅作旧类型 → 框架关系的降级转换，新增引用语义一律走 <c>ReferenceEdgeTypes</c>（禁在此扩表）。</remarks>
    private static bool MapRelation(RefModels.ReferenceType type, out ReferenceRelation relation)
    {
        switch (type)
        {
            case RefModels.ReferenceType.Includes:
                relation = ReferenceRelation.Owns;
                return true;
            case RefModels.ReferenceType.Dependency:
                relation = ReferenceRelation.Depends;
                return true;
            case RefModels.ReferenceType.Requires:
                relation = ReferenceRelation.Depends;
                return true;
            case RefModels.ReferenceType.Uses:
                relation = ReferenceRelation.Consumes;
                return true;
            case RefModels.ReferenceType.Extends:
                relation = ReferenceRelation.Provides;
                return true;
            default:
                relation = default;
                return false;
        }
    }
}