using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>镜像条目快照（键 + 状态 + 语义类别 + 激活 + 租约 + 时间）。</summary>
public sealed record TaskPoolEntryState(
    string TaskKey,
    string Status,
    TaskPoolItemKind Kind,
    bool Active,
    string? LeaseId,
    DateTime Timestamp);

/// <summary>
/// 任务状态镜像（可观测视图，通用任务池 canonical）：订阅 <see cref="ITaskPoolStateSource.TaskStateChanged"/>
/// 累积 TaskKey → 条目快照，供 UI / 监控消费。直读直写与事件订阅共用单点写入。
/// </summary>
public sealed class TaskPoolStateMirror
{
    private readonly ConcurrentDictionary<string, TaskPoolEntryState> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly EventHandler<TaskPoolStateChangedEventArgs> _handler;
    private ITaskPoolStateSource? _subscribedTo;

    public TaskPoolStateMirror()
        => _handler = OnStateChanged;

    public int Count => _states.Count;

    public IReadOnlyCollection<string> TaskKeys
        => _states.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>直写（状态 + 语义；供事件订阅与直读直写共用）。</summary>
    public void Set(string taskKey, string status,
        TaskPoolItemKind kind = TaskPoolItemKind.Executable,
        bool active = true,
        string? leaseId = null)
        => _states[taskKey] = new TaskPoolEntryState(
            taskKey, status, kind, active, leaseId, DateTime.UtcNow);

    public bool Remove(string taskKey)
        => _states.TryRemove(taskKey, out _);

    /// <summary>读取状态串（未观测返回 null）。</summary>
    public string? TryGet(string taskKey)
        => _states.TryGetValue(taskKey, out var s) ? s.Status : null;

    /// <summary>读取完整条目快照（未观测返回 null）。</summary>
    public TaskPoolEntryState? TryGetEntry(string taskKey)
        => _states.TryGetValue(taskKey, out var s) ? s : null;

    /// <summary>枚举镜像快照（键序排序）。</summary>
    public IReadOnlyList<TaskPoolEntryState> Entries()
        => _states.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToArray();

    public void EnsureSubscribed(ITaskPoolStateSource? source)
    {
        if (source is null) return;
        lock (_gate)
        {
            if (ReferenceEquals(_subscribedTo, source)) return;
            UnsubscribeInternal();
            source.TaskStateChanged += _handler;
            _subscribedTo = source;
        }
    }

    public void Detach(ITaskPoolStateSource? source)
    {
        lock (_gate)
        {
            if (source is not null && ReferenceEquals(_subscribedTo, source))
            {
                UnsubscribeInternal();
                _subscribedTo = null;
            }
        }
    }

    private void UnsubscribeInternal()
    {
        if (_subscribedTo is null) return;
        _subscribedTo.TaskStateChanged -= _handler;
        _subscribedTo = null;
    }

    private void OnStateChanged(object? sender, TaskPoolStateChangedEventArgs e)
        => Set(e.TaskKey, e.ToState.ToString(), e.Kind, e.Active, e.LeaseId);
}

