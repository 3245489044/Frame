namespace DataCenter.Protocol.Standards;

/// <summary>
/// 标准目录（三本台账的统一只读视图）：端口 / 数据 / 事件 / 替身 四视图 + 原始文本。
/// <para>
/// 实现落 <c>DataCenter.Standards</c>（模型读取 + 校验 + 装载）；本接口只出**声明面**，
/// 供 ① 校验（<c>StandardConsistency</c>）；② 装配播种（内核 <c>PortComposer.Seed</c> 吃原始 JSON 文本）。
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

    /// <summary>端口台账原始 JSON（含 ports/data 两段；供内核播种，内核只吃文本不吃文件）。</summary>
    string PortsJson { get; }

    /// <summary>事件台账原始 JSON（可为 null = 未提供事件源）。</summary>
    string? EventsJson { get; }
}
