using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 行读端口通用实现：域键 → <see cref="IStoreRowPort"/>（域目录经委托注入，框架不持有领域词）；
/// 载荷 = 行文档原文（与写回同物 ⇒ 读出→写回→再读逐字节相等）。
/// <para>未装配域 / 未提供列举器 ⇒ fail-closed 抛出（禁静默空集）。</para>
/// </summary>
public sealed class RowPortRecordReadPort : IRecordReadPort
{
    private readonly Func<string, IStoreRowPort?> _resolve;
    private readonly Func<string, string?, int, IReadOnlyList<string>>? _listKeys;
    private readonly Func<string, string, string, IReadOnlyDictionary<string, string>>? _fields;

    /// <param name="resolve">域键 → 行端口（未注册 ⇒ null）。</param>
    /// <param name="listKeys">域键 + 前缀 + 上限 → 键集（缺省 ⇒ <see cref="ReadManyAsync"/> fail-closed）。</param>
    /// <param name="fields">域键 + 真键 + 文档文本 → 呈现字段（缺省 ⇒ 空表；不伪造字段）。</param>
    public RowPortRecordReadPort(
        Func<string, IStoreRowPort?> resolve,
        Func<string, string?, int, IReadOnlyList<string>>? listKeys = null,
        Func<string, string, string, IReadOnlyDictionary<string, string>>? fields = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _listKeys = listKeys;
        _fields = fields;
    }

    /// <inheritdoc />
    public Task<RecordView?> ReadAsync(string domain, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var port = _resolve(domain)
            ?? throw new InvalidOperationException($"未装配行端口的域：{domain}（禁静默空行）");
        if (!port.TryRead(key, out var docText))
            return Task.FromResult<RecordView?>(null);

        return Task.FromResult<RecordView?>(new RecordView(
            domain, key, key,
            Fields(domain, key, docText),
            docText,
            "Live"));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecordView>> ReadManyAsync(string domain, string? keyPrefix, int limit, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ct.ThrowIfCancellationRequested();
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit 须为正整数（有界量化）。");

        if (_listKeys is null)
            throw new NotSupportedException($"域 {domain} 未提供键列举器：拒绝返回空集冒充「无数据」。");

        var port = _resolve(domain)
            ?? throw new InvalidOperationException($"未装配行端口的域：{domain}（禁静默空集）");

        var keys = _listKeys(domain, keyPrefix, limit);
        var views = new List<RecordView>(keys.Count);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            if (views.Count >= limit) break;
            if (!port.TryRead(key, out var docText)) continue;
            views.Add(new RecordView(domain, key, key, Fields(domain, key, docText), docText, "Live"));
        }
        return Task.FromResult<IReadOnlyList<RecordView>>(views);
    }

    /// <summary>呈现字段投影（未注入 ⇒ 空表；禁伪造字段值）。</summary>
    private IReadOnlyDictionary<string, string> Fields(string domain, string key, string docText)
        => _fields?.Invoke(domain, key, docText)
           ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
