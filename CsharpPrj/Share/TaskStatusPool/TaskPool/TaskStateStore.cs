using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务状态存储（按 TaskKey 的 ConcurrentDictionary，线程安全）。自包含实现，替代原 StateMachineStore。
/// 键比较与任务池其余字典一致（Ordinal，TaskKey 为幂等业务键/Guid 序列）。
/// </summary>
public sealed class TaskStateStore
{
    private readonly ConcurrentDictionary<string, TaskStateMachine> _states = new(StringComparer.Ordinal);

    public int Count => _states.Count;

    public TaskStateMachine GetOrCreate(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("TaskKey cannot be empty", nameof(id));
        return _states.GetOrAdd(id, _ => new TaskStateMachine());
    }

    public TaskStateMachine? Get(string id)
        => string.IsNullOrWhiteSpace(id) ? null : _states.TryGetValue(id, out var s) ? s : null;

    public IEnumerable<string> GetAllIds() => _states.Keys.ToArray();
}
