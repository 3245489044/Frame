using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>租约键：内容寻址（同输入=同租约）。InputDigest 由解析后入参计算。</summary>
public readonly record struct NodeLeaseKey(string WorkflowId, string NodeKey, string InputDigest);

/// <summary>租约状态（单飞：Running 唯一执行者；Completed 可复用；Faulted 失败传染）。</summary>
public enum NodeLeaseState
{
    Running = 0,
    Completed = 1,
    Faulted = 2,
}

/// <summary>租约结果：执行者拿 Running；等待者拿同一结果；读复用 Completed。</summary>
public sealed record NodeLeaseOutcome(
    NodeLeaseKey Key,
    NodeLeaseState State,
    bool Reused,
    bool Waited,
    string? OwnerTaskKey,
    string? Diagnostic,
    IReadOnlyDictionary<string, object?>? Ports = null);

/// <summary>节点单飞注册表（C6）。跨组允许读复用（Completed + digest 相同）。</summary>
public interface INodeLeaseRegistry
{
    Task<NodeLeaseOutcome> AcquireOrWaitAsync(NodeLeaseKey key, string taskKey, string? groupKey, CancellationToken ct);

    /// <summary>发布终态；返回 false = 非 owner/未知键（调用方须可见化，禁静默当作已发布）。</summary>
    bool Publish(NodeLeaseKey key, string taskKey, NodeLeaseState state, string? diagnostic,
        IReadOnlyDictionary<string, object?>? ports = null);
}
