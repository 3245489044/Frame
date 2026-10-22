using System;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// attempt 的唯一写入者（AX-ZR SingleAttemptWriter）：其余位置出现 attempt++ / round++ 均为违规。
/// 实例租约（非静态）：一个 run 一个账本，跨 run 不共享配额与摘要（消除全局污染）。
/// <see cref="RetryGrant"/> 默认 0（框架语义上不重试），仅装配点显式授予且入审计。
/// </summary>
public sealed class AttemptLedger
{
    private readonly object _sync = new();
    private int _grant;
    private int _used;
    private string? _lastDigest;

    /// <summary>装配点授予的重试配额（默认 0）。</summary>
    public int RetryGrant { get { lock (_sync) return _grant; } }

    /// <summary>已消耗的重试次数（= 当前 ExecutionUnit 内的 attempt 序号）。</summary>
    public int Attempt { get { lock (_sync) return _used; } }

    /// <summary>已消耗的重试次数（兼容旧名）。</summary>
    public int Used { get { lock (_sync) return _used; } }

    /// <summary>上次执行的输入摘要（重试必须变更）。</summary>
    public string? LastDigest { get { lock (_sync) return _lastDigest; } }

    /// <summary>账本是否允许推进（配额未用尽）。</summary>
    public bool CanAdvance { get { lock (_sync) return _grant > 0 && _used < _grant; } }

    /// <summary>run 边界：清空消耗与摘要（不改变装配点授予的配额）。</summary>
    public void Begin(string? inputDigest = null)
    {
        lock (_sync)
        {
            _used = 0;
            _lastDigest = inputDigest;
        }
    }

    /// <summary>装配点授予配额（一次性、入审计；0 = 不重试）。</summary>
    public void Grant(int times)
    {
        if (times < 0) throw new ArgumentOutOfRangeException(nameof(times));
        lock (_sync)
        {
            _grant = times;
            _used = 0;
            _lastDigest = null;
        }
    }

    /// <summary>仅 Retry 应用点可调：推进一次 attempt 并记录新摘要。</summary>
    public void Advance(RetryPlan plan, string newDigest)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(newDigest)) throw new ArgumentException("新摘要不可为空。", nameof(newDigest));
        lock (_sync)
        {
            if (_grant <= 0 || _used >= _grant) throw new InvalidOperationException(FailureCodes.UnjustifiedRetry);
            _used++;
            _lastDigest = newDigest;
        }
    }

    /// <summary>重置账本（新 run 起点；不改变装配点授予的配额）。</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _used = 0;
            _lastDigest = null;
        }
    }
}
