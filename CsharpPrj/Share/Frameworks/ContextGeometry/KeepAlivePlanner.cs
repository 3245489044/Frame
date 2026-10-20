namespace Share.ContextGeometry;

/// <summary>keepalive 作用域快照（宿主传入；内核不取时钟）。</summary>
public readonly record struct KeepAliveScope(string Scope, DateTimeOffset LastUsedUtc, int ReplayedCount = 0);

/// <summary>keepalive 参数（来自声明面；间隔/TTL/次数三重有界）。</summary>
public readonly record struct KeepAliveOptions(int IntervalSeconds, int TtlSeconds, int MaxReplays);

/// <summary>
/// keepalive 计划器（纯函数）：只输出「该不该重放」，不产生时间/线程/IO。
/// <para>判据唯一 = <see cref="KeepAlivePolicy.ShouldReplay"/>（本类不做第二套阈值）。</para>
/// </summary>
public static class KeepAlivePlanner
{
    /// <summary>到期需重放的作用域清单（未到期/已过期/超次数 ⇒ 不入选；到期 ⇒ 恰一次）。</summary>
    public static IReadOnlyList<string> Plan(
        IEnumerable<KeepAliveScope>? scopes, DateTimeOffset nowUtc, KeepAliveOptions options)
    {
        var due = new List<string>();
        foreach (var scope in scopes ?? Enumerable.Empty<KeepAliveScope>())
        {
            if (string.IsNullOrWhiteSpace(scope.Scope)) continue;
            if (KeepAlivePolicy.ShouldReplay(
                    scope.LastUsedUtc, nowUtc,
                    options.IntervalSeconds, options.TtlSeconds,
                    scope.ReplayedCount, options.MaxReplays))
            {
                due.Add(scope.Scope);
            }
        }
        return due;
    }
}
