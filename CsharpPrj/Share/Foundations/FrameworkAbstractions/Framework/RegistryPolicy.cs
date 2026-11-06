#nullable enable

using System;
using System.Collections.Generic;

namespace FrameworkAbstractions.Framework;

/// <summary>重复键处置策略。</summary>
public enum RegistryDuplicatePolicy
{
    /// <summary>重复键即抛（装配期显式失败）。</summary>
    Throw,

    /// <summary>同实例幂等忽略；不同实例仍抛。</summary>
    IdempotentSameReference,

    /// <summary>首次注册者胜出，后续同键静默忽略。</summary>
    KeepFirst,

    /// <summary>后注册者胜出，槽位（插入位置）保持不变。</summary>
    ReplaceInPlace,
}

/// <summary>枚举顺序策略。</summary>
public enum RegistryEnumerationOrder
{
    /// <summary>注册顺序（插入序）。</summary>
    Insertion,

    /// <summary>显式声明序（<c>order</c> 升序，未声明者排后并按插入序）。</summary>
    DeclaredOrder,

    /// <summary>按键排序（稳定输出）。</summary>
    KeySorted,
}

/// <summary>
/// 注册表策略：把既有六个键值表的分歧口径显式化。
/// <para><b>不提供比较器默认值</b>：键是机器标识，大小写是否等价必须由调用方显式声明。</para>
/// </summary>
public sealed record RegistryPolicy<TKey, TValue> where TKey : notnull
{
    /// <summary>键相等比较器（必填，无隐式默认）。</summary>
    public required IEqualityComparer<TKey> Comparer { get; init; }

    /// <summary>重复键处置（默认抛：装配期失败优于静默覆盖）。</summary>
    public RegistryDuplicatePolicy Duplicate { get; init; } = RegistryDuplicatePolicy.Throw;

    /// <summary>枚举顺序（默认注册序）。</summary>
    public RegistryEnumerationOrder Order { get; init; } = RegistryEnumerationOrder.Insertion;

    /// <summary>按键排序时使用的比较器（仅 <see cref="RegistryEnumerationOrder.KeySorted"/> 使用）。</summary>
    public IComparer<TKey>? KeyComparer { get; init; }

    /// <summary>键校验（返回 null = 通过；返回文案 = 拒绝原因）。</summary>
    public Func<TKey, string?>? ValidateKey { get; init; }

    /// <summary>条目校验（返回 null = 通过；返回文案 = 拒绝原因）。</summary>
    public Func<TKey, TValue, string?>? ValidateEntry { get; init; }

    /// <summary>未命中时 Require 抛出的异常工厂；null = <see cref="InvalidOperationException"/>。</summary>
    public Func<TKey, Exception>? MissingExceptionFactory { get; init; }

    /// <summary>重复键抛出的异常工厂；null = <see cref="InvalidOperationException"/>。</summary>
    public Func<TKey, Exception>? DuplicateExceptionFactory { get; init; }

    /// <summary>替换生效时的回调（如 Refresh 要求停旧源）；在锁外调用。</summary>
    public Action<TKey, TValue>? OnReplaced { get; init; }

    /// <summary>空集合是否允许通过校验（默认不允许：空集合不是"完备"）。</summary>
    public bool AllowEmptyDeclaration { get; init; }
}

/// <summary>
/// 六张既有键值表的语义 profile（不含任何领域类型）。
/// <para>用途：迁移前先对齐口径；profile 只声明策略，不复制表实现。</para>
/// </summary>
public static class RegistryProfiles
{
    /// <summary>Catalog：忽略大小写、同实例幂等/异实例抛、装配序。</summary>
    public static RegistryPolicy<string, TValue> Catalog<TValue>() => new()
    {
        Comparer = StringComparer.OrdinalIgnoreCase,
        KeyComparer = StringComparer.OrdinalIgnoreCase,
        Duplicate = RegistryDuplicatePolicy.IdempotentSameReference,
        Order = RegistryEnumerationOrder.Insertion,
        MissingExceptionFactory = static key => new InvalidOperationException($"未注册目录源：{key}"),
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };

    /// <summary>Refresh：Ordinal、同键异实例替换（旧源由回调停）、装配序。</summary>
    public static RegistryPolicy<string, TValue> Refresh<TValue>() => new()
    {
        Comparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        Duplicate = RegistryDuplicatePolicy.ReplaceInPlace,
        Order = RegistryEnumerationOrder.Insertion,
        MissingExceptionFactory = static key => new InvalidOperationException($"未登记刷新项：{key}"),
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };

    /// <summary>Observability 契约表：Ordinal、后写覆盖、按键排序、未命中抛。</summary>
    public static RegistryPolicy<string, TValue> ObservabilityContract<TValue>() => new()
    {
        Comparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        Duplicate = RegistryDuplicatePolicy.ReplaceInPlace,
        Order = RegistryEnumerationOrder.KeySorted,
        MissingExceptionFactory = static key => new InvalidOperationException($"未注册观测契约：{key}"),
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };

    /// <summary>Boundary 契约表：Ordinal、重复即抛、按键排序。</summary>
    public static RegistryPolicy<string, TValue> BoundaryContract<TValue>() => new()
    {
        Comparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        Duplicate = RegistryDuplicatePolicy.Throw,
        Order = RegistryEnumerationOrder.KeySorted,
        MissingExceptionFactory = static key => new InvalidOperationException($"未注册边界契约：{key}"),
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };

    /// <summary>DataContract：Ordinal、首次优先、装配序（静态全局语义特殊，迁移暂缓）。</summary>
    public static RegistryPolicy<string, TValue> DataContract<TValue>() => new()
    {
        Comparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        Duplicate = RegistryDuplicatePolicy.KeepFirst,
        Order = RegistryEnumerationOrder.Insertion,
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };

    /// <summary>SpecPort：Ordinal、重复即抛、按键排序、未命中走 TryGet（不抛）。</summary>
    public static RegistryPolicy<string, TValue> SpecPort<TValue>() => new()
    {
        Comparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        Duplicate = RegistryDuplicatePolicy.Throw,
        Order = RegistryEnumerationOrder.KeySorted,
        ValidateKey = static key => string.IsNullOrWhiteSpace(key) ? "键不得为空。" : null,
    };
}
