using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Tasks;

namespace Operation.HierarchyMethod.Hierarchy;

/// <summary>
/// 三层层级统一管理门面默认实现（LLM-free 组合根）：编排「引用管理（IHierarchyReferenceManager）」与
/// 「三层实例（节点输出 / 任务簿记）」—— 只编排不复制写边原语（SingleImplementation / TH-12）。
/// 装配可空：未装配引用端口 → 查询空集 / 写 no-op；未装配节点/任务端口 → 对应持久化跳过。不阻断 DB-only 路径。
/// </summary>
public sealed class HierarchyManager : IHierarchyManager
{
    private readonly IHierarchyReferenceManager? _reference;
    private readonly INodeOutputStore? _nodes;
    private readonly Func<TaskRecord, CancellationToken, Task>? _saveTask;
    private readonly Func<string, CancellationToken, Task>? _deleteTask;

    /// <param name="reference">引用管理端口（可空；实现 = DataStoreDriven.ReferenceGrounding）。本门面及三层实例只依赖此接口、零 LiteDB 具体类型。</param>
    /// <param name="nodes">节点输出存储（可空；ur_nodes NodeType=output）。</param>
    /// <param name="saveTask">任务簿记保存端口（可空；由组合根注入任务池持久化）。</param>
    /// <param name="deleteTask">任务簿记删除端口（可空）。</param>
    public HierarchyManager(
        IHierarchyReferenceManager? reference = null,
        INodeOutputStore? nodes = null,
        Func<TaskRecord, CancellationToken, Task>? saveTask = null,
        Func<string, CancellationToken, Task>? deleteTask = null)
    {
        _reference = reference;
        _nodes = nodes;
        _saveTask = saveTask;
        _deleteTask = deleteTask;
    }

    public async Task<HistoryRecord> CreateHistoryAsync(string historyId, string? name = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        if (_reference is not null)
            await _reference.EnsureHistoryAsync(historyId, ct).ConfigureAwait(false);
        return new HistoryRecord(historyId, name ?? historyId);
    }

    public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default)
        => _reference is null ? Task.FromResult(false) : _reference.DeleteHistoryAsync(historyId, ct);

    public async Task RegisterTaskAsync(string taskKey, string? historyId = null, TaskRecord? spec = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        var record = spec ?? new TaskRecord();
        record.TaskKey = taskKey;
        if (!string.IsNullOrWhiteSpace(historyId))
            record.HistoryId = historyId;

        if (_saveTask is not null)
            await _saveTask(record, ct).ConfigureAwait(false);

        if (_reference is not null && !string.IsNullOrWhiteSpace(historyId))
        {
            // 归拢到 history：建历史根 + 挂 history→task 边（幂等；二者均顺带注册 task/task 端点镜像）。
            await _reference.EnsureHistoryAsync(historyId, ct).ConfigureAwait(false);
            await _reference.AttachHistoryTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);
        }
    }

    public async Task DeleteTaskAsync(string taskKey, CancellationToken ct = default)
    {
        if (_deleteTask is not null)
            await _deleteTask(taskKey, ct).ConfigureAwait(false);
        // 引用侧 task 端点的级联清边由 DataStoreDriven 守卫（RemoveTaskMirrorAsync）负责，
        // 本门面仅负责实例删除；如需同步图侧可在组合根注入 reference 的 task 镜像删除委托。
    }

    public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _reference is null ? Task.CompletedTask : _reference.LinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _reference is null ? Task.CompletedTask : _reference.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetTasksAsync(string historyId, CancellationToken ct = default)
        => _reference is null ? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())
            : _reference.GetHistoryTaskKeysAsync(historyId, ct);

    public Task<IReadOnlyList<string>> GetNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _reference is null ? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())
            : _reference.GetTaskNodeKeysAsync(taskKey, workflowId, ct);

    public Task<IReadOnlyList<string>> GetHistoryNodesAsync(string historyId, string workflowId, CancellationToken ct = default)
        => _reference is null ? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())
            : _reference.GetHistoryNodeKeysAsync(historyId, workflowId, ct);

    public Task PersistNodeOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
        => _nodes is null ? Task.CompletedTask : _nodes.WriteOutputAsync(workflowId, nodeKey, port, value, ct);
}
