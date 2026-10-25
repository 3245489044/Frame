using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 线程安全内存 KV 表单源（U6）：<c>Get/Put/Delete/List(threadId)</c>。
/// <para>供 <c>TaskPoolStoreFactory.CreateInMemory</c> 与各域内存替身复用（禁各自复制一份字典）。</para>
/// </summary>
public sealed class InMemoryKvTable<T> where T : class
{
    private readonly ConcurrentDictionary<string, T> _rows = new(StringComparer.Ordinal);

    /// <summary>全部键（稳定序）。</summary>
    public IReadOnlyList<string> Keys => _rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>键是否存在。</summary>
    public bool Contains(string key) => !string.IsNullOrWhiteSpace(key) && _rows.ContainsKey(key);

    /// <summary>取行（缺失 ⇒ null）。</summary>
    public T? Get(string? key)
        => !string.IsNullOrWhiteSpace(key) && _rows.TryGetValue(key!, out var value) ? value : null;

    /// <summary>写入/覆盖。</summary>
    public void Put(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _rows[key] = value;
    }

    /// <summary>删除（返回是否命中）。</summary>
    public bool Delete(string? key)
        => !string.IsNullOrWhiteSpace(key) && _rows.TryRemove(key!, out _);

    /// <summary>按前缀列举（稳定序），供会话/历史类按作用域读取。</summary>
    public IReadOnlyList<T> List(Func<string, bool>? predicate = null)
        => _rows
            .Where(kv => predicate is null || predicate(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToList();

    /// <summary>清空（测试复位）。</summary>
    public void Clear() => _rows.Clear();
}
