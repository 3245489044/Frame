using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Identity;
using Operation.DataStoreDriven.Relations;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 实体存在性**唯一注册表**（GP-2 / E1）：每实体类型一实现，禁止各命令层自判。
/// <list type="bullet">
/// <item>history：走键面投影候选行键（规范 <c>history:{id}</c> + 只读兼容裸键）；</item>
/// <item>task / node：存储面键（task = 裸键；node 先按身份文法反解，失败回落原串）；</item>
/// <item>未映射类型：<see cref="IsMapped"/> = false，<see cref="ExistsAsync"/> = false（内核记 <c>probe_unmapped</c>，不与端点缺失混同）；</item>
/// <item>已映射但行端口未装配：fail-fast（<c>existence-unattached</c>），禁伪装成不存在。</item>
/// </list>
/// </summary>
public sealed class EntityExistenceRegistry : IEntityExistenceProbe
{
    private static readonly HashSet<string> MappedTypes = new(StringComparer.Ordinal)
    {
        "history",
        "task",
        "node",
    };

    private readonly Func<string, IStoreRowPort?> _rowPortOf;

    /// <param name="rowPortOf">实体类型 → 该类型所在 Store 的行端口（未装配 ⇒ null）。</param>
    public EntityExistenceRegistry(Func<string, IStoreRowPort?> rowPortOf)
    {
        ArgumentNullException.ThrowIfNull(rowPortOf);
        _rowPortOf = rowPortOf;
    }

    /// <inheritdoc />
    public bool IsMapped(string entityType) => MappedTypes.Contains(entityType);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsMapped(entityType)) return Task.FromResult(false);

        var port = _rowPortOf(entityType)
            ?? throw new InvalidOperationException(
                $"existence-unattached: entityType={entityType}, id={id}（该类型行端口未装配，禁伪装成不存在）");

        foreach (var key in CandidateKeys(entityType, id))
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (port.TryRead(key, out _)) return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private static IEnumerable<string> CandidateKeys(string entityType, string id)
    {
        if (entityType == "history") return NodeEndpointIds.HistoryRowKeys(id);

        if (entityType == "node"
            && NodeEndpointIds.TryParseNodeIdentity(id, workflowId: null, out var nodeKey)
            && nodeKey.Length > 0
            && !string.Equals(nodeKey, id, StringComparison.Ordinal))
        {
            return new[] { id, nodeKey };
        }

        return new[] { id };
    }
}
