using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// task→node 批量建链**单一入口**（DUP-2 / DUP-3 / RED-1 收敛）：
/// 键归一（<see cref="NodeKeyPlan"/> 单源）+ 去重 + 端点镜像 + 批量建边。
/// <para>
/// 调用方（工作流函数 / 对账 / UI 适配）只调本类，禁各自实现键规则或循环拼边；
/// 批量语义由 <see cref="IReferenceMaintenance.EnsureTaskNodeLinksAsync"/> 承接（生产实现覆盖为一次内核调用）。
/// </para>
/// </summary>
public static class TaskNodeLinkPlan
{
    /// <summary>键归一 + 去重（保序；空白键丢弃）。</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyCollection<string>? nodeKeys)
        => NodeKeyPlan.Normalize(nodeKeys);

    /// <summary>批量建链：归一 + 去重 + 镜像 + 幂等建边；返回实际处理键数（空输入 ⇒ 0）。</summary>
    public static async Task<int> LinkAsync(
        IReferenceMaintenance maintenance,
        string taskKey,
        string? workflowId,
        IReadOnlyCollection<string>? nodeKeys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(maintenance);
        if (string.IsNullOrWhiteSpace(taskKey)) return 0;

        var keys = Normalize(nodeKeys);
        if (keys.Count == 0) return 0;

        return await maintenance
            .EnsureTaskNodeLinksAsync(taskKey, NodeKeyPlan.NormalizeNamespace(workflowId), keys, ct)
            .ConfigureAwait(false);
    }
}

