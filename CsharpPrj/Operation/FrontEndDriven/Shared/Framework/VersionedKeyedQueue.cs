#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 版本化键去重队列（通用框架）：以键为单位的 latest-wins 队列。
/// 同键重复注册仅保留最新版本（旧版本在消费时经 <see cref="TryConsumeIfLatest"/> 判定为过期而丢弃），
/// 供串行单消费者取快照遍历执行最新版本。收敛 RenderRequestQueue 的 scope+version 去重存储。
/// </summary>
/// <typeparam name="TKey">键类型。</typeparam>
/// <typeparam name="TEntry">条目类型。</typeparam>
public sealed class VersionedKeyedQueue<TKey, TEntry>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Item> _latest;
    private long _nextVersion;
    private int _draining;

    private sealed record Item(long Version, TEntry Entry);

    public VersionedKeyedQueue(IEqualityComparer<TKey>? keyComparer = null)
    {
        _latest = keyComparer == null
            ? new ConcurrentDictionary<TKey, Item>()
            : new ConcurrentDictionary<TKey, Item>(keyComparer);
    }

    public int Count => _latest.Count;

    /// <summary>注册新版本（latest-wins），返回分配的单调版本号。</summary>
    public long Register(TKey key, TEntry entry)
    {
        var version = Interlocked.Increment(ref _nextVersion);
        _latest[key] = new Item(version, entry);
        return version;
    }

    /// <summary>当前全部条目快照（含各自版本号），供单消费者遍历。</summary>
    public IReadOnlyList<(TKey Key, TEntry Entry, long Version)> Snapshot()
    {
        var items = new List<(TKey, TEntry, long)>(_latest.Count);
        foreach (var kvp in _latest)
            items.Add((kvp.Key, kvp.Value.Entry, kvp.Value.Version));
        return items;
    }

    public bool TryGet(TKey key, out TEntry entry, out long version)
    {
        if (_latest.TryGetValue(key, out var item))
        {
            entry = item.Entry;
            version = item.Version;
            return true;
        }
        entry = default!;
        version = 0;
        return false;
    }

    public bool TryRemove(TKey key)
        => _latest.TryRemove(key, out _);

    /// <summary>
    /// 仅当 key 仍持有 <paramref name="snapshotVersion"/> 时才移除并返回其条目（true）；
    /// 否则（期间被更新为更新版本，或已被移除）返回 false——调用方据此丢弃过期条目。
    /// </summary>
    public bool TryConsumeIfLatest(TKey key, long snapshotVersion, out TEntry entry)
    {
        if (_latest.TryGetValue(key, out var item) && item.Version == snapshotVersion)
        {
            _latest.TryRemove(key, out _);
            entry = item.Entry;
            return true;
        }
        entry = default!;
        return false;
    }

    /// <summary>
    /// 单消费者排空（通用 drain 辅助）：对每个仍有最新条目的键仅执行最新版本（过期丢弃），执行后出队。
    /// 收敛 RenderRequestQueue / LayoutScheduler 各自手写的 drain 循环（_draining 重入守卫 + 快照遍历
    /// + TryConsumeIfLatest 版本去重 + try/finally 复位 + while 吞执行期新请求）。
    /// <paramref name="execute"/> 返回 false 表示「终止本 pass 的剩余遍历」（与原实现的 <c>break</c> 语义逐行一致，
    /// 供重排风暴护栏等使用）；返回 true 继续遍历。同步消费方把 work 包成 <c>Task.FromResult(bool)</c> 返回即可。
    /// </summary>
    /// <param name="execute">最新版本条目执行委托 (key, entry, version) → 是否继续遍历（false=break）。含结果契约（TCS）的消费方在此完成回调。</param>
    /// <param name="onStale">过期条目丢弃回调（期间被更新的旧版本；无结果契约场景可为 null）。</param>
    /// <param name="maxPasses">单次 drain 最大迭代轮数（预算护栏，防御 execute 反复注册造成的无限循环；默认无上限）。</param>
    public async Task DrainAllAsync(
        Func<TKey, TEntry, long, Task<bool>> execute,
        Action<TKey, TEntry>? onStale = null,
        int maxPasses = int.MaxValue)
    {
        if (execute == null) throw new ArgumentNullException(nameof(execute));
        if (maxPasses <= 0) maxPasses = 1;

        if (Interlocked.Exchange(ref _draining, 1) == 1)
            return;

        try
        {
            int pass = 0;
            while (_latest.Count > 0 && pass++ < maxPasses)
            {
                foreach (var (key, entry, version) in Snapshot())
                {
                    // 版本去重：仅当仍是最新版本才执行（期间被更新则本次跳过，由新版本负责）
                    if (!TryConsumeIfLatest(key, version, out var current))
                    {
                        onStale?.Invoke(key, entry);
                        continue;
                    }

                    if (!await execute(key, current, version).ConfigureAwait(true))
                        break; // 调用方请求终止本 pass 剩余遍历（与原 `break` 语义一致）
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
        }
    }
}
