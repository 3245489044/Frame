namespace DataCenter.Protocol.Standards;

using DataCenter.Protocol.Dataflow;

/// <summary>
/// 标准目录（五本台账的统一只读视图）：端口 / 数据 / 事件 / 替身 / 数据产品 + 原始文本。
/// <para>
/// 实现落 <c>DataCenter.Standards</c>（模型读取 + 校验 + 装载）；本接口只出**声明面**，
/// 供 ① 校验（<c>StandardConsistency</c>）；② 装配播种（内核 <c>PortComposer.Seed</c> 吃原始 JSON 文本）。
/// </para>
/// <para>
/// 第五本台账（数据产品）以<b>接口默认成员</b>接入：未提供产品台账的实现方（含测试替身）
/// 自动得到空视图，不构成破坏性契约变更（DP1 的装载期阻断只在提供 productsJson 时生效）。
/// </para>
/// </summary>
public interface IStandardCatalog
{
    /// <summary>端口标准（键 Ordinal 升序）。</summary>
    IReadOnlyList<PortStandard> Ports { get; }

    /// <summary>数据标准（键 Ordinal 升序）。</summary>
    IReadOnlyList<DataStandardItem> Data { get; }

    /// <summary>事件标准（键 Ordinal 升序）。</summary>
    IReadOnlyList<EventStandard> Events { get; }

    /// <summary>替身标准（端口键 Ordinal 升序）。</summary>
    IReadOnlyList<FakeStandard> Fakes { get; }

    /// <summary>数据产品标准（第五本台账；默认空 = 未提供产品台账）。</summary>
    IReadOnlyList<DataProductSpec> Products => [];

    /// <summary>产品台账原始 JSON（默认 null = 未提供）。</summary>
    string? ProductsJson => null;

    /// <summary>端口台账原始 JSON（含 ports/data 两段；供内核播种，内核只吃文本不吃文件）。</summary>
    string PortsJson { get; }

    /// <summary>事件台账原始 JSON（可为 null = 未提供事件源）。</summary>
    string? EventsJson { get; }
}
