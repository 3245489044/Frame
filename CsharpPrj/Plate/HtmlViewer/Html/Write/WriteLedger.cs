#nullable enable

using System;
using System.Collections.Concurrent;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 写去重账本（唯一去重状态源）：语义固定 <c>NextVersion → Decide → 仅落地成功 Commit</c>。
/// <para>线程安全（<see cref="ConcurrentDictionary{TKey,TValue}"/>）；宿主注入 <see cref="IWriteArbiter"/> 时
/// 由其持有版本/指纹（跨进程/跨层共享），否则本地记账（零回归回退面）。</para>
/// <para>写失败只记 Ok=false（下次同内容仍会重试），禁预提交。</para>
/// </summary>
internal sealed class WriteLedger
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWriteArbiter? _arbiter;

    public WriteLedger(IWriteArbiter? arbiter = null) => _arbiter = arbiter;

    /// <summary>写准入：true = 需落地；false = 内容未变/版本过期（不触 DOM）。</summary>
    public bool TryAdmit(string slotKey, string fingerprint, out long version)
    {
        if (_arbiter is not null)
        {
            version = _arbiter.NextVersion(slotKey);
            return _arbiter.Decide(slotKey, version, fingerprint) == WriteVerdict.Apply;
        }

        version = 0;
        return !(_entries.TryGetValue(slotKey, out var prev)
                 && prev.Ok
                 && string.Equals(prev.Fingerprint, fingerprint, StringComparison.Ordinal));
    }

    /// <summary>落地成功后提交（推进 baseline；失败路径禁调用）。</summary>
    public void Commit(string slotKey, long version, string fingerprint)
    {
        _entries[slotKey] = new Entry(fingerprint, true);
        _arbiter?.Commit(slotKey, version, fingerprint);
    }

    /// <summary>落地失败记账（Ok=false ⇒ 下次同内容重试）。</summary>
    public void MarkFailure(string slotKey, string fingerprint)
        => _entries[slotKey] = new Entry(fingerprint, false);

    private readonly record struct Entry(string Fingerprint, bool Ok);
}
