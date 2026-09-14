namespace DataCenter.Protocol.Standards;

/// <summary>
/// 数据标准项（跨切数据项；<c>ports.json</c> 的 <c>data[x]</c> 的中立形态）：
/// 键 / 方向 / 作用域 / 字段 / 生产方 / 消费方 / 版本。
/// <para>方向取值由产品台账既有约定给出（<c>In</c>/<c>Out</c>/<c>Both</c>）；本层不解释语义。</para>
/// </summary>
public sealed record DataStandardItem(
    string Key,
    string Direction,
    string Scope,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Producers,
    IReadOnlyList<string> Consumers,
    int Version);
