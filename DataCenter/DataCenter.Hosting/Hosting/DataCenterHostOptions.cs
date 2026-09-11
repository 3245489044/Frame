using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Projection;
using DataCenter.Protocol.Registry;
using DataCenter.Protocol.Roots;
using Operation.StateBlock.Core;
using Operation.StateBlock.Generation;
using Operation.StateBlock.Projection;
using Operation.StateBlock.Registry;

namespace DataCenter.Hosting;

/// <summary>
/// 单根宿主装配选项 —— Boot 的全部输入（显式装配，无隐藏默认根）。
/// 每项均可注入：宿主设施（ALC/引擎 cell）经此进入，Core 保持零设施依赖。
/// </summary>
public sealed class DataCenterHostOptions
{
    /// <summary>根单元的构造工厂（宿主可注入自有 <see cref="IDataCell"/> 实现）；null = 内存单元。</summary>
    public Func<IDataCell>? RootCellFactory { get; init; }

    /// <summary>scope 分区子单元工厂（分层写路径的 scope 分区承载）。</summary>
    public IDataCellFactory PartitionCellFactory { get; init; } = new InMemoryDataCellFactory();

    /// <summary>图投影器；null = Core 的通用结构投影器（分层状态 → CanonicalForm）。</summary>
    public IStateProjector? Projector { get; init; }

    /// <summary>值编解码（投影载荷化）；默认 JSON 编解码。</summary>
    public IStateValueCodec ValueCodec { get; init; } = new JsonValueCodec();

    /// <summary>类型/键注册表（tier/nodeKind/edgeKind 只读消费面）；默认示例集。</summary>
    public IStateRegistry Registry { get; init; } = GraphRegistry.CreateDefault();

    /// <summary>初始代际（generation/fingerprint/schemaVersion）。</summary>
    public IStateGeneration Generation { get; init; } = GenerationState.Initial;

    /// <summary>运行时槽装配委托（域键分区注册装配产物）；null = 不注册槽（只读空分区）。</summary>
    public Action<IRuntimeSlot>? ConfigureSlots { get; init; }

    /// <summary>根单元名（诊断用）。</summary>
    public string RootCellName { get; init; } = "datacenter-root";
}
