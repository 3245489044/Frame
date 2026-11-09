namespace CallCenter.LlmGate.Rules;

/// <summary>
/// 时间窗口计数（指纹重复 / 每会话速率）。单写者（锁内），时钟由调用方显式传入。
/// 计数在裁决前读取、裁决后写入（拒绝也计入，保证循环持续被识别）。
/// </summary>
public sealed class WindowCounters
{
    private readonly object _sync = new();
    private readonly Queue<(string Fingerprint, string SessionKey, DateTimeOffset At)> _entries = new();

    public (int FingerprintCount, int SessionCalls) Snapshot(
        string fingerprint, string sessionKey, DateTimeOffset now, int loopWindowSec)
    {
        lock (_sync)
        {
            Prune(now, loopWindowSec);
            var fingerprintCount = 0;
            var sessionCount = 0;
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    fingerprintCount++;
                }

                if (string.Equals(entry.SessionKey, sessionKey, StringComparison.Ordinal)
                    && now - entry.At <= TimeSpan.FromMinutes(1))
                {
                    sessionCount++;
                }
            }

            return (fingerprintCount, sessionCount);
        }
    }

    public void Add(string fingerprint, string sessionKey, DateTimeOffset now)
    {
        lock (_sync)
        {
            _entries.Enqueue((fingerprint, sessionKey, now));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private void Prune(DateTimeOffset now, int loopWindowSec)
    {
        var keep = TimeSpan.FromSeconds(Math.Max(loopWindowSec, 60));
        while (_entries.Count > 0 && now - _entries.Peek().At > keep)
        {
            _entries.Dequeue();
        }
    }
}
