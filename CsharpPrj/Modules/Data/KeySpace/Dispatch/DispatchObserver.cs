using System.Collections.Concurrent;

namespace Modules.KeySpace.Dispatch;

/// <summary>
/// 分发观察者（Observe 段）：按作用域比较相邻轮次指纹，判断 macro 是否推进。
/// <para>
/// 纪律：本类**不持有 noGain 计数**（空转账单一写入仍在治理层）；只回答「本轮是否有推进」，
/// 由调用方把结论喂给唯一计数器。有在途租约（<c>inFlight</c>）时按推进计（防误报，R4）。
/// </para>
/// </summary>
public sealed class DispatchObserver
{
    private readonly ConcurrentDictionary<string, PlanFingerprint> _last = new(StringComparer.Ordinal);

    /// <summary>观察一次规划结果；返回 macro 是否推进（指纹变化 ∨ 有产物增量 ∨ 有在途 ⇒ 推进）。</summary>
    public bool Observe(string scope, PlanFingerprint fingerprint, bool inFlight = false, int completedDelta = 0)
    {
        var key = string.IsNullOrWhiteSpace(scope) ? "__default__" : scope;
        if (inFlight || completedDelta > 0)
        {
            _last[key] = fingerprint;
            return true;
        }

        if (!_last.TryGetValue(key, out var previous))
        {
            _last[key] = fingerprint;
            return true;
        }

        if (previous.Equals(fingerprint))
        {
            return false;
        }

        _last[key] = fingerprint;
        return true;
    }

    /// <summary>移除作用域状态（容器关闭/会话结束；防跨会话残留）。</summary>
    public void Forget(string scope)
    {
        var key = string.IsNullOrWhiteSpace(scope) ? "__default__" : scope;
        _last.TryRemove(key, out _);
    }
}
