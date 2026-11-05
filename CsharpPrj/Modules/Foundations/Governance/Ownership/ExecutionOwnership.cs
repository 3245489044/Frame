using System;
using System.Collections.Concurrent;

namespace Modules.Governance.Ownership;

/// <summary>驱动来源（执行域归属的合法取值）。</summary>
public enum DriverKind
{
    TaskPool,
    SessionFold,
    Manual,
    Replay,
}

/// <summary>
/// 执行归属键：只允许「可执行实体」作为被驱动对象（实体种类 + 实体标识）。
/// <para>实体种类由调用方按声明给出；本内核不做领域判断。</para>
/// </summary>
public readonly record struct OwnershipKey(string EntityKind, string EntityId)
{
    public static OwnershipKey Of(string entityKind, string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityKind))
            throw new ArgumentException("实体种类不得为空", nameof(entityKind));
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("实体标识不得为空", nameof(entityId));
        return new OwnershipKey(entityKind.Trim(), entityId.Trim());
    }

    public override string ToString() => $"{EntityKind}:{EntityId}";
}

/// <summary>归属租约（令牌）：持有者 + 到期时刻 + 唯一租约号。</summary>
public sealed record OwnershipLease(
    OwnershipKey Key,
    DriverKind Driver,
    string LeaseId,
    string OwnerId,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt);

/// <summary>获取结果（拒绝必须带诊断码，禁静默排队/覆盖）。</summary>
public sealed record OwnershipResult(bool Acquired, OwnershipLease? Lease, string? Diagnostic)
{
    public static OwnershipResult Granted(OwnershipLease lease) => new(true, lease, null);

    public static OwnershipResult Rejected(string diagnostic, OwnershipLease? existing = null)
        => new(false, existing, diagnostic);
}

/// <summary>归属快照（诊断面）。</summary>
public sealed record OwnershipSnapshot(bool Held, OwnershipLease? Lease, bool Expired);

/// <summary>
/// 执行归属（A3：驱动必须先取令牌）。原子获取；已持有 ⇒ 拒绝（不阻塞、不覆盖）。
/// </summary>
public interface IExecutionOwnership
{
    OwnershipResult TryAcquire(OwnershipKey key, DriverKind driver, string ownerId, TimeSpan ttl);

    /// <summary>释放；租约号不匹配 ⇒ 拒绝（防误释放他人租约）。</summary>
    bool Release(OwnershipKey key, string leaseId);

    OwnershipSnapshot Inspect(OwnershipKey key);
}

/// <summary>
/// 进程内归属实现（唯一驱动令牌）：TTL 到期可被回收，回收事实由 <see cref="Reclaimed"/> 上报。
/// </summary>
public sealed class InMemoryExecutionOwnership : IExecutionOwnership
{
    private readonly ConcurrentDictionary<OwnershipKey, OwnershipLease> _leases = new();
    private readonly TimeProvider _clock;

    public InMemoryExecutionOwnership(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>租约回收可见化（载荷 = 被回收租约）；无订阅者时为空实现。</summary>
    public event Action<OwnershipLease>? Reclaimed;

    public OwnershipResult TryAcquire(OwnershipKey key, DriverKind driver, string ownerId, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return OwnershipResult.Rejected("EXEC_DRIVER_OWNER_MISSING");

        var now = _clock.GetUtcNow();
        var expires = now + (ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl);

        while (true)
        {
            if (_leases.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt > now)
                    return OwnershipResult.Rejected("EXEC_DRIVER_CONFLICT", existing);

                var reclaimed = _leases.TryUpdate(key, NewLease(key, driver, ownerId, now, expires), existing);
                if (!reclaimed) continue;
                Reclaimed?.Invoke(existing);
                return OwnershipResult.Granted(NewLease(key, driver, ownerId, now, expires));
            }

            var lease = NewLease(key, driver, ownerId, now, expires);
            if (_leases.TryAdd(key, lease)) return OwnershipResult.Granted(lease);
        }
    }

    public bool Release(OwnershipKey key, string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) return false;
        if (!_leases.TryGetValue(key, out var existing)) return false;
        if (!string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal)) return false;
        return _leases.TryRemove(new System.Collections.Generic.KeyValuePair<OwnershipKey, OwnershipLease>(key, existing));
    }

    public OwnershipSnapshot Inspect(OwnershipKey key)
    {
        if (!_leases.TryGetValue(key, out var lease)) return new OwnershipSnapshot(false, null, false);
        var expired = lease.ExpiresAt <= _clock.GetUtcNow();
        return new OwnershipSnapshot(true, lease, expired);
    }

    private static OwnershipLease NewLease(
        OwnershipKey key, DriverKind driver, string ownerId, DateTimeOffset now, DateTimeOffset expires)
        => new(key, driver, Guid.NewGuid().ToString("N"), ownerId.Trim(), now, expires);
}

/// <summary>
/// 不受限实现（未装配归属内核时的显式形态：恒授予，仍产出租约以供观测）。
/// 生产必须装配 <see cref="InMemoryExecutionOwnership"/>。
/// </summary>
public sealed class UnrestrictedExecutionOwnership : IExecutionOwnership
{
    public static UnrestrictedExecutionOwnership Instance { get; } = new();

    public OwnershipResult TryAcquire(OwnershipKey key, DriverKind driver, string ownerId, TimeSpan ttl)
        => OwnershipResult.Granted(new OwnershipLease(
            key, driver, Guid.NewGuid().ToString("N"), ownerId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow + ttl));

    public bool Release(OwnershipKey key, string leaseId) => true;

    public OwnershipSnapshot Inspect(OwnershipKey key) => new(false, null, false);
}
