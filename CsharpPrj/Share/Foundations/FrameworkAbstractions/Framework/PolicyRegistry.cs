#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameworkAbstractions.Framework;

/// <summary>注册表中的一行（声明序位置一并带出）。</summary>
public sealed record RegistryRow<TKey, TValue>(TKey Key, TValue Value, int? Order, int Position);

/// <summary>
/// 策略化键值注册表（通用基座）：一个实现覆盖既有六张表与三种"有序声明注册表"的语义。
/// <para><b>显式口径</b>（不继承任何既有表的默认）：</para>
/// <list type="bullet">
/// <item>插入顺序：始终保序；枚举顺序由 <see cref="RegistryEnumerationOrder"/> 决定。</item>
/// <item>重复键：由 <see cref="RegistryDuplicatePolicy"/> 决定；默认 <c>Throw</c>。</item>
/// <item>大小写：由调用方必填的 <see cref="RegistryPolicy{TKey,TValue}.Comparer"/> 决定；
/// 基座自身不设默认比较器，避免"Catalog 式忽略大小写"与"Refresh 式 Ordinal"互相污染。</item>
/// <item>冲突胜者：<c>KeepFirst</c>=先注册者胜；<c>ReplaceInPlace</c>=后注册者胜且保留槽位；
/// <c>IdempotentSameReference</c>=同实例无变化、异实例抛（无胜者）；<c>Throw</c>=无胜者（失败）。</item>
/// </list>
/// <para>枚举结果一律为快照数组（消除"只读视图其实是活 List"的并发缺陷）。</para>
/// </summary>
public sealed class PolicyRegistry<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly RegistryPolicy<TKey, TValue> _policy;
    private readonly Dictionary<TKey, Row> _byKey;
    private readonly List<Row> _rows = new();
    private long _sequence;

    public PolicyRegistry(RegistryPolicy<TKey, TValue> policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _byKey = new Dictionary<TKey, Row>(policy.Comparer);
    }

    /// <summary>生效策略（只读）。</summary>
    public RegistryPolicy<TKey, TValue> Policy => _policy;

    /// <summary>已注册条目数。</summary>
    public int Count
    {
        get { lock (_gate) return _rows.Count; }
    }

    /// <summary>键快照（按策略枚举顺序）。</summary>
    public IReadOnlyList<TKey> Keys => Snapshot().Select(static row => row.Key).ToArray();

    /// <summary>值快照（按策略枚举顺序）。</summary>
    public IReadOnlyList<TValue> Values => Snapshot().Select(static row => row.Value).ToArray();

    /// <summary>行快照（含声明序与位置；按策略枚举顺序）。</summary>
    public IReadOnlyList<RegistryRow<TKey, TValue>> Rows => Snapshot()
        .Select((row, index) => new RegistryRow<TKey, TValue>(row.Key, row.Value, row.Order, index))
        .ToArray();

    /// <summary>
    /// 注册一个条目。返回 true = 新增或替换生效；false = 幂等/首次优先导致的忽略。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    /// <param name="order">显式声明序（仅 <see cref="RegistryEnumerationOrder.DeclaredOrder"/> 参与排序）。</param>
    public bool Register(TKey key, TValue value, int? order = null)
    {
        ValidateEntryOrThrow(key, value);

        TKey? replacedKey = default;
        TValue? replacedValue = default;
        var replaced = false;
        var added = false;

        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var existing))
            {
                switch (_policy.Duplicate)
                {
                    case RegistryDuplicatePolicy.IdempotentSameReference:
                        if (ReferenceEquals(existing.Value, value)) return false;
                        throw DuplicateOrThrow(key);

                    case RegistryDuplicatePolicy.KeepFirst:
                        return false;

                    case RegistryDuplicatePolicy.Throw:
                        throw DuplicateOrThrow(key);

                    case RegistryDuplicatePolicy.ReplaceInPlace:
                        _byKey[key] = new Row(key, value, order, existing.Sequence);
                        _rows[_rows.IndexOf(existing)] = _byKey[key];
                        replacedKey = key;
                        replacedValue = existing.Value;
                        replaced = true;
                        break;

                    default:
                        throw new InvalidOperationException($"未知重复键策略：{_policy.Duplicate}");
                }
            }
            else
            {
                var row = new Row(key, value, order, ++_sequence);
                _byKey[key] = row;
                _rows.Add(row);
                added = true;
            }
        }

        if (replaced && _policy.OnReplaced is { } onReplaced)
            onReplaced(replacedKey!, replacedValue!);

        return added || replaced;
    }

    /// <summary>
    /// 显式替换（装配期热换）：存在则原位替换（保留槽位），不存在则等价注册。
    /// <para>该操作不受 <see cref="RegistryDuplicatePolicy.Throw"/> 约束——调用方已显式表达替换意图。</para>
    /// </summary>
    public bool Replace(TKey key, TValue value, int? order = null)
    {
        ValidateEntryOrThrow(key, value);

        TKey? replacedKey = default;
        TValue? replacedValue = default;
        var replaced = false;

        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var existing))
            {
                _byKey[key] = new Row(key, value, order, existing.Sequence);
                _rows[_rows.IndexOf(existing)] = _byKey[key];
                replacedKey = key;
                replacedValue = existing.Value;
                replaced = true;
            }
            else
            {
                var row = new Row(key, value, order, ++_sequence);
                _byKey[key] = row;
                _rows.Add(row);
            }
        }

        if (replaced && _policy.OnReplaced is { } onReplaced)
            onReplaced(replacedKey!, replacedValue!);

        return true;
    }

    /// <summary>尝试取值（未命中返回 false，不抛）。</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var row))
            {
                value = row.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>取值；未命中返回 null（仅用于可空值类型或引用类型）。</summary>
    public TValue? Get(TKey key)
        => TryGet(key, out var value) ? value : default;

    /// <summary>取值；未命中调用 <see cref="RegistryPolicy{TKey,TValue}.MissingExceptionFactory"/>（默认 <see cref="InvalidOperationException"/>）。</summary>
    public TValue Require(TKey key)
    {
        if (TryGet(key, out var value)) return value;
        throw _policy.MissingExceptionFactory?.Invoke(key)
              ?? new InvalidOperationException($"未注册键：{key}");
    }

    /// <summary>是否已注册。</summary>
    public bool Contains(TKey key)
    {
        lock (_gate) return _byKey.ContainsKey(key);
    }

    /// <summary>移除一个条目；返回 true = 实际移除（槽位序号不再复用）。</summary>
    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var row)) return false;
            _byKey.Remove(key);
            _rows.Remove(row);
            return true;
        }
    }

    /// <summary>清空（测试隔离：使装配态可复现）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _byKey.Clear();
            _rows.Clear();
            _sequence = 0;
        }
    }

    /// <summary>按策略枚举顺序返回键的位置索引（未注册返回 -1）。</summary>
    public int IndexOf(TKey key)
    {
        var snapshot = Snapshot();
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (_policy.Comparer.Equals(snapshot[i].Key, key)) return i;
        }

        return -1;
    }

    /// <summary>
    /// 完备性校验：空集合默认不通过（修正"空集合当成功"的空洞通过）；
    /// 非空时把值快照交给调用方的校验器。
    /// </summary>
    public IReadOnlyList<string> Validate(Func<IReadOnlyList<TValue>, IReadOnlyList<string>>? validator = null)
    {
        var values = Values;
        if (values.Count == 0 && !_policy.AllowEmptyDeclaration)
            return new[] { "注册表为空：至少需要一个声明。" };
        return validator?.Invoke(values) ?? Array.Empty<string>();
    }

    private void ValidateEntryOrThrow(TKey key, TValue value)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (_policy.ValidateKey?.Invoke(key) is { Length: > 0 } keyError)
            throw new ArgumentException(keyError, nameof(key));
        if (_policy.ValidateEntry?.Invoke(key, value) is { Length: > 0 } entryError)
            throw new ArgumentException(entryError, nameof(value));
    }

    private Exception DuplicateOrThrow(TKey key)
        => _policy.DuplicateExceptionFactory?.Invoke(key)
           ?? new InvalidOperationException($"键重复：{key}");

    private List<Row> Snapshot()
    {
        lock (_gate)
        {
            var rows = new List<Row>(_rows);
            switch (_policy.Order)
            {
                case RegistryEnumerationOrder.Insertion:
                    break;
                case RegistryEnumerationOrder.DeclaredOrder:
                    rows.Sort(static (a, b) =>
                    {
                        var orderA = a.Order ?? int.MaxValue;
                        var orderB = b.Order ?? int.MaxValue;
                        return orderA != orderB ? orderA.CompareTo(orderB) : a.Sequence.CompareTo(b.Sequence);
                    });
                    break;
                case RegistryEnumerationOrder.KeySorted:
                    var comparer = _policy.KeyComparer ?? KeyComparerOf(_policy.Comparer);
                    rows.Sort((a, b) => comparer.Compare(a.Key, b.Key));
                    break;
                default:
                    throw new InvalidOperationException($"未知枚举顺序：{_policy.Order}");
            }

            return rows;
        }
    }

    private static IComparer<TKey> KeyComparerOf(IEqualityComparer<TKey> comparer)
    {
        // 无显式 Cmp 时退化为 Ordinal 字符串序；仅影响枚举顺序，不影响相等判定。
        return Comparer<TKey>.Create(static (a, b) =>
            string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal));
    }

    private sealed record Row(TKey Key, TValue Value, int? Order, long Sequence);
}
