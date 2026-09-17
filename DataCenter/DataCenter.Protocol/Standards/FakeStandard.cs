namespace DataCenter.Protocol.Standards;

/// <summary>
/// 替身标准项（<c>fakes[x]</c> / 用例侧 <c>given ports</c> 的中立形态）：
/// 端口键 / 替身种类（<c>recording|counting|fixed|sequence|notExpected</c>，= 执行器支持集）/ seed（可选字面量）/ 版本。
/// </summary>
public sealed record FakeStandard(
    string PortKey,
    string Kind,
    string? Seed,
    int Version);
