using BasicMethod.Declarative.Ledger;
using DataCenter.Protocol.Standards.Descriptors;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口逐层键闭包（<see cref="PortImplVocabulary"/> 的键部分）：键声明的**单一真源 = SSD**（<see cref="StandardDescriptors.All"/>）。
/// </summary>
public static partial class PortImplVocabulary
{
    private static LedgerKeyClosure? _keyClosure;

    /// <summary>
    /// 分层键闭包（通用框架 <see cref="LedgerKeyClosure"/>）：层名 → 合法键集，
    /// 「未知键 fail-fast」的**唯一实现**。
    /// <para>
    /// **键声明单一真源 = SSD（<see cref="StandardDescriptors.All"/>）**：本表由各层
    /// <see cref="LayerDescriptor.Layer"/> → <see cref="LayerDescriptor.Keys"/> 派生，不再手抄键表（G1 收口）。
    /// </para>
    /// <para>惰性装配：SSD 各层成员含由 <c>ExprSlots</c> 派生的键（静态初始化序不定），故首次访问时构造（防悬空引用）。</para>
    /// <para>
    /// 例外（登记，非静默）：<c>composedArg</c> 层当前**不纳入**键闭包（保持现状：读取器不校验该层，
    /// 键集仅由对拍守护）；是否纳入为独立裁决，不因本次派生而改变行为。
    /// </para>
    /// </summary>
    private static LedgerKeyClosure KeyClosure => _keyClosure ??= new(
        StandardDescriptors.All
            .Where(layer => !string.Equals(layer.Layer, "composedArg", StringComparison.Ordinal))
            .Select(layer => (layer.Layer, layer.Keys)));

    /// <summary>给定层的键白名单（未登记层 → null = 该层不做未知键校验）；取值域 = 框架键闭包。</summary>
    public static IReadOnlyList<string>? KeysOf(string layer)
        => KeyClosure.IsKnownLayer(layer) ? KeyClosure.KeysOf(layer) : null;

    /// <summary>
    /// 收集层内**未登记键**（保持出现序、去重；未登记层不判）——**唯一实现** = 框架 <see cref="LedgerKeyClosure.EnsureKnown"/>；
    /// 违规文案由调用方注入（域文案留消费方）。
    /// </summary>
    public static void EnsureKnownLayerKeys(
        string layer,
        IEnumerable<string> presentKeys,
        string path,
        Func<string, string, string> messageFactory,
        List<string> violations)
        => KeyClosure.EnsureKnown(layer, presentKeys, path, messageFactory, violations);
}
