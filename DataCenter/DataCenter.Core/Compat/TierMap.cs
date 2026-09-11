using DataCenter.Abstractions.Cells;
using TConfig;

namespace Operation.StateBlock.Core.Compat;

/// <summary>
/// 分层枚举映射 —— 契约层 <see cref="StorageLayer"/>（Plate 层既有枚举）与端口层 <see cref="DataTier"/> 的唯一转换点。
/// 领域词汇不进出本文件：此处只做同构枚举的一一映射，使 Core 内部实现只依赖端口。
/// </summary>
internal static class TierMap
{
    public static DataTier ToTier(this StorageLayer layer) => layer switch
    {
        StorageLayer.Config => DataTier.Config,
        StorageLayer.Session => DataTier.Session,
        StorageLayer.Transient => DataTier.Transient,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, $"不支持的存储层级: {layer}"),
    };

    public static StorageLayer ToLayer(this DataTier tier) => tier switch
    {
        DataTier.Config => StorageLayer.Config,
        DataTier.Session => StorageLayer.Session,
        DataTier.Transient => StorageLayer.Transient,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, $"不支持的分层标识: {tier}"),
    };
}
