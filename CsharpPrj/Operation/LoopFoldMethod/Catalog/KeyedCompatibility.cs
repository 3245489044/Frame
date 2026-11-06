namespace Operation.LoopFoldMethod.Catalog;

/// <summary>
/// 键目录转发门面：实现单源 = <c>Modules.Foundations.Collections.Keyed.KeyedCatalog&lt;TDescriptor&gt;</c>
/// （L1 通用件）。保留本命名空间仅为既有二进制消费方（Agent.Modules）的类型契约，不承载第二套实现。
/// </summary>
/// <typeparam name="TDescriptor">域描述类型。</typeparam>
public sealed class KeyedCatalog<TDescriptor> : Modules.Foundations.Collections.Keyed.KeyedCatalog<TDescriptor>
{
    /// <inheritdoc />
    public KeyedCatalog(IEnumerable<TDescriptor> all, Func<TDescriptor, string> keyOf)
        : base(all, keyOf)
    {
    }
}

/// <summary>
/// 域键分派转发门面：实现单源 = <c>Modules.Foundations.Collections.Keyed.KeyedDispatcher&lt;TService&gt;</c>。
/// </summary>
/// <typeparam name="TService">域服务类型。</typeparam>
public sealed class KeyedDispatcher<TService> : Modules.Foundations.Collections.Keyed.KeyedDispatcher<TService>
{
    /// <inheritdoc />
    public KeyedDispatcher(IReadOnlyDictionary<string, TService> services, string defaultKey)
        : base(services, defaultKey)
    {
    }
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
