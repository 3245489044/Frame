namespace Operation.LoopFoldMethod.Catalog;

/// <summary>
/// 键目录转发门面：实现单源 = <c>Modules.Foundations.Collections.Keyed.KeyedCatalog&lt;TDescriptor&gt;</c>（L1 通用件）。
/// <para>
/// 保留本命名空间仅为既有二进制消费方（Agent.Modules）的类型契约；门面只转发、无算法，不构成第二套实现。
/// 采用**组合**（不继承）以免把 Modules 程序集写入门面公开元数据。
/// </para>
/// </summary>
/// <typeparam name="TDescriptor">域描述类型。</typeparam>
public sealed class KeyedCatalog<TDescriptor>
{
    private readonly Modules.Foundations.Collections.Keyed.KeyedCatalog<TDescriptor> _inner;

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedCatalog{TDescriptor}.KeyedCatalog" />
    public KeyedCatalog(IEnumerable<TDescriptor> all, Func<TDescriptor, string> keyOf)
        => _inner = new Modules.Foundations.Collections.Keyed.KeyedCatalog<TDescriptor>(all, keyOf);

    /// <summary>全部描述（声明序即装配序）。</summary>
    public IReadOnlyList<TDescriptor> All => _inner.All;

    /// <summary>已登记域键（声明序）。</summary>
    public IReadOnlyList<string> Keys => _inner.Keys;

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedCatalog{TDescriptor}.TryGet" />
    public bool TryGet(string? key, out TDescriptor descriptor) => _inner.TryGet(key, out descriptor);

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedCatalog{TDescriptor}.Require" />
    public TDescriptor Require(string key) => _inner.Require(key);
}

/// <summary>
/// 域键分派转发门面：实现单源 = <c>Modules.Foundations.Collections.Keyed.KeyedDispatcher&lt;TService&gt;</c>；
/// 组合转发，无算法。
/// </summary>
/// <typeparam name="TService">域服务类型。</typeparam>
public sealed class KeyedDispatcher<TService>
{
    private readonly Modules.Foundations.Collections.Keyed.KeyedDispatcher<TService> _inner;

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedDispatcher{TService}.KeyedDispatcher" />
    public KeyedDispatcher(IReadOnlyDictionary<string, TService> services, string defaultKey)
        => _inner = new Modules.Foundations.Collections.Keyed.KeyedDispatcher<TService>(services, defaultKey);

    /// <summary>已装配的域键集。</summary>
    public IReadOnlyDictionary<string, TService> Services => _inner.Services;

    /// <summary>默认域键。</summary>
    public string DefaultKey => _inner.DefaultKey;

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedDispatcher{TService}.TryGet" />
    public bool TryGet(string? key, out TService service) => _inner.TryGet(key, out service);

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedDispatcher{TService}.For" />
    public TService? For(string? key) => _inner.For(key);
}

/// <summary>键选择合并转发门面（纯函数，单源 = Modules KeyedSelection）。</summary>
public static class KeyedSelection
{
    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedSelection.MergeExplicitOrder" />
    public static IReadOnlyList<string> MergeExplicitOrder(
        IReadOnlyList<string> keys, IReadOnlyList<string>? order)
        => Modules.Foundations.Collections.Keyed.KeyedSelection.MergeExplicitOrder(keys, order);
}

/// <summary>装配消息转发门面（单源 = Modules KeyedTextItem）。</summary>
/// <param name="Key">条目键。</param>
/// <param name="Stage">阶段。</param>
/// <param name="Text">正文。</param>
/// <param name="Round">轮次。</param>
public sealed record KeyedTextItem(string Key, string Stage, string Text, int Round);

/// <summary>有序文本装配转发门面（单源 = Modules KeyedTextAssembler）。</summary>
public static class KeyedTextAssembler
{
    /// <summary>未归属轮次哨兵（与 Modules 单源一致）。</summary>
    public const int UnattributedRound = Modules.Foundations.Collections.Keyed.KeyedTextAssembler.UnattributedRound;

    /// <inheritdoc cref="Modules.Foundations.Collections.Keyed.KeyedTextAssembler.Assemble" />
    public static IReadOnlyList<KeyedTextItem> Assemble(
        IEnumerable<(string Key, IReadOnlyDictionary<string, string> Fields)> items,
        string textField = "text",
        string stageField = "kind",
        string roundField = "round")
    {
        return Modules.Foundations.Collections.Keyed.KeyedTextAssembler
            .Assemble(items, textField, stageField, roundField)
            .Select(item => new KeyedTextItem(item.Key, item.Stage, item.Text, item.Round))
            .ToArray();
    }
}
