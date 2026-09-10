using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务状态镜像（可观测视图，通用任务池 canonical，SingleImplementation / TH-12）：
/// 订阅 <see cref="ITaskPoolStateSource.TaskStateChanged"/> 累积 TaskKey → Status 字符串映射，供 UI / 监控消费。
/// 直读直写（<see cref="Set"/> / <see cref="Remove"/>）与事件订阅共用单点写入，消除散落赋值。
/// 按源实例订阅：<see cref="EnsureSubscribed"/> 换源即重订、<see cref="Detach"/> 解绑退订，
/// 避免「静态布尔永不重置」的委托残留泄漏。
/// </summary>
public sealed class TaskPoolStateMirror
{
    private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly EventHandler<TaskPoolStateChangedEventArgs> _handler;
    private ITaskPoolStateSource? _subscribedTo;

    public TaskPoolStateMirror()
        => _handler = OnStateChanged;

    /// <summary>已观测任务数。</summary>
    public int Count => _states.Count;

    /// <summary>已观测任务键集合（键序排序快照，供列表展示）。</summary>
    public IReadOnlyCollection<string> TaskKeys
        => _states.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>直写（供事件订阅与直读直写共用，消除散落赋值）。</summary>
    public void Set(string taskKey, string status)
        => _states[taskKey] = status;

    /// <summary>直写移除（删除任务后同步清理）。返回是否命中（不存在返回 false）。</summary>
    public bool Remove(string taskKey)
        => _states.TryRemove(taskKey, out _);

    /// <summary>读取（key → status；未观测返回 null）。</summary>
    public string? TryGet(string taskKey)
        => _states.TryGetValue(taskKey, out var s) ? s : null;

    /// <summary>枚举镜像快照（键序排序，供列表/概览展示）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries()
        => _states.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray();

    /// <summary>确保对当前事件源订阅（按源实例订阅：换源即重订、未装配不订；幂等）。</summary>
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

    /// <summary>当前源解绑时退订（清空订阅引用，避免旧源委托残留形成内存泄漏）。</summary>
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
        => Set(e.TaskKey, e.ToState.ToString());
}
