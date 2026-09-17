using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// 标准目录实现（设计文档 §3.2 <c>StandardCatalog</c>）：惰性、确定性排序（键 Ordinal 升序）、键唯一。
/// <para>
/// 四视图 = 端口 / 数据 / 事件 / 替身；原始文本随目录同行（供内核 <c>PortComposer.Seed</c> 播种——
/// 内核只吃文本，不读文件）。
/// </para>
/// </summary>
public sealed class StandardCatalog : IStandardCatalog
{
    private readonly Dictionary<string, PortStandard> _ports;
    private readonly Dictionary<string, DataStandardItem> _data;
    private readonly Dictionary<string, EventStandard> _events;
    private readonly Dictionary<string, FakeStandard> _fakes;

    private StandardCatalog(
        IReadOnlyList<PortStandard> ports,
        IReadOnlyList<DataStandardItem> data,
        IReadOnlyList<EventStandard> events,
        IReadOnlyList<FakeStandard> fakes,
        string portsJson,
        string? eventsJson,
        string? fakesJson)
    {
        Ports = ports;
        Data = data;
        Events = events;
        Fakes = fakes;
        PortsJson = portsJson;
        EventsJson = eventsJson;
        FakesJson = fakesJson;
        _ports = ports.ToDictionary(p => p.Key, StringComparer.Ordinal);
        _data = data.ToDictionary(d => d.Key, StringComparer.Ordinal);
        _events = events.ToDictionary(e => e.Key, StringComparer.Ordinal);
        _fakes = fakes.ToDictionary(f => f.PortKey, StringComparer.Ordinal);
    }

    public IReadOnlyList<PortStandard> Ports { get; }

    public IReadOnlyList<DataStandardItem> Data { get; }

    public IReadOnlyList<EventStandard> Events { get; }

    public IReadOnlyList<FakeStandard> Fakes { get; }

    public string PortsJson { get; }

    public string? EventsJson { get; }

    /// <summary>替身台账原始 JSON（可为 null = 无仓库级默认替身）。</summary>
    public string? FakesJson { get; }

    /// <summary>端口键集合（Ordinal 升序）。</summary>
    public IReadOnlyList<string> PortKeys => Ports.Select(p => p.Key).ToArray();

    /// <summary>数据键集合（Ordinal 升序；C3/C4 的取值域）。</summary>
    public IReadOnlyList<string> DataKeys => Data.Select(d => d.Key).ToArray();

    /// <summary>事件键集合（Ordinal 升序；C2 的取值域）。</summary>
    public IReadOnlyList<string> EventKeys => Events.Select(e => e.Key).ToArray();

    /// <summary>装载三本台账（<paramref name="portsJson"/> 必给：含 ports/data 两段）。</summary>
    public static StandardCatalog Load(string portsJson, string? eventsJson = null, string? fakesJson = null)
        => new(
            StandardJson.ReadPorts(portsJson),
            StandardJson.ReadData(portsJson),
            eventsJson is null ? [] : StandardJson.ReadEvents(eventsJson),
            fakesJson is null ? [] : StandardJson.ReadFakes(fakesJson),
            portsJson,
            eventsJson,
            fakesJson);

    /// <summary>装载并校验（校验失败即抛 <see cref="StandardViolationException"/>，阻塞装载）。</summary>
    public static StandardCatalog LoadValidated(
        string portsJson,
        string? eventsJson = null,
        string? fakesJson = null,
        StandardConsistencyOptions? options = null)
    {
        var catalog = Load(portsJson, eventsJson, fakesJson);
        StandardConsistency.EnsureValid(catalog, options);
        return catalog;
    }

    /// <summary>取端口标准（未登记 → null）。</summary>
    public PortStandard? FindPort(string key) => _ports.GetValueOrDefault(key);

    /// <summary>取数据标准（未登记 → null）。</summary>
    public DataStandardItem? FindData(string key) => _data.GetValueOrDefault(key);

    /// <summary>取事件标准（未登记 → null）。</summary>
    public EventStandard? FindEvent(string key) => _events.GetValueOrDefault(key);

    /// <summary>取替身标准（未登记 → null）。</summary>
    public FakeStandard? FindFake(string portKey) => _fakes.GetValueOrDefault(portKey);
}
