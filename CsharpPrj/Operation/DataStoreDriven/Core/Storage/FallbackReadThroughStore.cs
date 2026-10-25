using System;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 回退读透装饰器单源（U7）：主存储优先、回退兜底、可配双删。
/// <para>迁移期 <c>LegacyFallbackSessionTranscriptStore</c> 降为本类型的一次实例化。</para>
/// </summary>
public sealed class FallbackReadThroughStore<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Func<TKey, CancellationToken, Task<TValue?>> _primaryRead;
    private readonly Func<TKey, CancellationToken, Task<TValue?>> _fallbackRead;
    private readonly Func<TKey, CancellationToken, Task<bool>> _primaryDelete;
    private readonly Func<TKey, CancellationToken, Task<bool>>? _fallbackDelete;

    public FallbackReadThroughStore(
        Func<TKey, CancellationToken, Task<TValue?>> primaryRead,
        Func<TKey, CancellationToken, Task<TValue?>> fallbackRead,
        Func<TKey, CancellationToken, Task<bool>> primaryDelete,
        Func<TKey, CancellationToken, Task<bool>>? fallbackDelete = null)
    {
        _primaryRead = primaryRead ?? throw new ArgumentNullException(nameof(primaryRead));
        _fallbackRead = fallbackRead ?? throw new ArgumentNullException(nameof(fallbackRead));
        _primaryDelete = primaryDelete ?? throw new ArgumentNullException(nameof(primaryDelete));
        _fallbackDelete = fallbackDelete;
    }

    /// <summary>主存储优先，未命中回退兜底。</summary>
    public async Task<TValue?> GetAsync(TKey key, CancellationToken ct = default)
    {
        var value = await _primaryRead(key, ct).ConfigureAwait(false);
        if (value is not null) return value;
        return await _fallbackRead(key, ct).ConfigureAwait(false);
    }

    /// <summary>主删 + （可配）回退删；返回任一命中。</summary>
    public async Task<bool> DeleteAsync(TKey key, CancellationToken ct = default)
    {
        var removed = await _primaryDelete(key, ct).ConfigureAwait(false);
        if (_fallbackDelete is null) return removed;
        var fallbackRemoved = await _fallbackDelete(key, ct).ConfigureAwait(false);
        return removed || fallbackRemoved;
    }
}
