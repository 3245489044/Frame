using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Modules.KeySpace.Dispatch;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 休眠激活扫描器（DTC Activate 段唯一实现）：dormant 不是「等待中的合法态」，超时/重试超限必有终态。
/// <para>触发源语义：Explicit 不自动超时；MemberAttached/Scheduler 到点 → 失败终态。</para>
/// </summary>
public sealed class DormantActivationSweep
{
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public DormantActivationSweep(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>登记休眠条件（非法条件返回 false，由调用方 fail-fast）。</summary>
    public bool Register(string taskKey, ActivationCondition condition)
    {
        if (string.IsNullOrWhiteSpace(taskKey) || !condition.IsValid)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        _entries[taskKey] = new Entry(condition, now + TimeSpan.FromSeconds(condition.TimeoutSeconds ?? 0));
        return true;
    }

    /// <summary>目标已激活/取消登记（离开 dormant）。</summary>
    public void Activate(string taskKey) => _entries.TryRemove(taskKey, out _);

    /// <summary>当前登记数（判据：不随时间无界增长）。</summary>
    public int Count => _entries.Count;

    /// <summary>收集到期的终态项；未超重试上限者内部续期（返回不包含）。</summary>
    public IReadOnlyList<DormantTimeout> CollectDue()
    {
        var now = _clock.GetUtcNow();
        var due = new List<DormantTimeout>();

        foreach (var pair in _entries)
        {
            if (!pair.Value.Condition.AutoTimeout || now < pair.Value.Deadline)
            {
                continue;
            }

            if (pair.Value.Attempts < pair.Value.Condition.MaxRetries)
            {
                pair.Value.Attempts++;
                pair.Value.Deadline = now.AddSeconds(pair.Value.Condition.TimeoutSeconds!.Value);
                continue;
            }

            _entries.TryRemove(pair.Key, out _);
            due.Add(new DormantTimeout(pair.Key, pair.Value.Condition.MaxRetries > 0));
        }

        return due;
    }

    private sealed class Entry
    {
        public Entry(ActivationCondition condition, DateTimeOffset deadline)
        {
            Condition = condition;
            Deadline = deadline;
        }

        public ActivationCondition Condition { get; }

        public DateTimeOffset Deadline { get; set; }

        public int Attempts { get; set; }
    }
}

/// <summary>到期终态项（DeadLetter = 重试超限，进死信；否则 failed 可重试）。</summary>
public readonly record struct DormantTimeout(string TaskKey, bool DeadLetter);
