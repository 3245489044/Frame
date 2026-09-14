using Strategy.Observability.Bus;
using Strategy.Observability.Entity;
using Strategy.Observability.Persistence;
using Strategy.Observability.Query;
using Strategy.Observability.Registry;

namespace Operation.StatisticalDriven.Observe;

/// <summary>
/// 统计语义观测组合根：装配本模块的语义契约（statistical_semantics.json）进通用内核。
/// 机制与语义分离：本类只注入语义实例与容器（registry/store/sinks/bus），不改内核（Strategy.Observability）。
/// </summary>
/// <remarks>
/// 装配归属（与 Agent 语义并存的方案 A）：本组合根面向<b>独立宿主</b>（CI/CD、Produce 等非 Agent 场景）。
/// Agent 进程中与 <c>AgentObservability</c> 并存时，不要用本组合根重复 Configure <see cref="Obs"/>（进程级单例），
/// 改为调 <see cref="LoadContracts"/> / <see cref="RegisterInto"/> 由 Agent 组合根合并进同一 registry。
/// </remarks>
public sealed class StatisticalObservability : IAsyncDisposable
{
    /// <summary>收集总线（E4）：单入口 Emit + 显式 FlushAsync；埋点可经 <see cref="Obs.Emit"/>（独立宿主已 Configure）。</summary>
    public IObservabilityBus Bus { get; }

    /// <summary>事件区（E5）：append-only，进程内唯一事实源；查询同源。</summary>
    public IEventStore Store { get; }

    /// <summary>投影原语（E6）：Timeline / EntityChain / Aggregate；成本投影经此查询。</summary>
    public ObservabilityQuery Query { get; }

    /// <summary>实体身份分配源（E1）。</summary>
    public EntityIdFactory Ids { get; } = new();

    private StatisticalObservability(IObservabilityBus bus, IEventStore store)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Query = new ObservabilityQuery(store);
    }

    /// <summary>加载本模块语义契约（statistical_semantics.json，嵌入资源；可覆盖为外部实例）。</summary>
    public static IReadOnlyList<EventContract> LoadContracts(string? semanticsJson = null)
        => ObservabilitySemanticsJson.Load(semanticsJson ?? ReadEmbeddedSemantics());

    /// <summary>把本模块语义契约并入既有注册表（方案 A：Agent 组合根合并用）。重复注册静默忽略（TryAdd）。</summary>
    public static void RegisterInto(IObservabilityContractRegistry registry, string? semanticsJson = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var c in LoadContracts(semanticsJson))
            registry.Register(c);
    }

    /// <summary>
    /// 独立宿主装配一次统计语义观测（默认 Configure <see cref="Obs"/>）。
    /// 与 Agent 同进程时请改用 <see cref="RegisterInto"/>，勿重复 Configure。
    /// </summary>
    /// <param name="eventStorePath">事件区文件路径（append-only JSONL，E5）。</param>
    /// <param name="extraContracts">追加契约（如 Agent 语义 json 加载的 <see cref="EventContract"/> 列表）；缺省 null。</param>
    /// <param name="extraSinks">追加的实时投影 Sink；缺省 null。</param>
    /// <param name="configureObs">是否配置进程级静态门面 <see cref="Obs"/>；独立宿主 true，与 Agent 并存时 false。</param>
    public static async Task<StatisticalObservability> CreateAsync(
        string eventStorePath,
        IEnumerable<EventContract>? extraContracts = null,
        IEnumerable<IObservabilitySink>? extraSinks = null,
        bool configureObs = true,
        CancellationToken ct = default)
    {
        var registry = new ObservabilityContractRegistry();
        RegisterInto(registry);
        if (extraContracts is not null)
            foreach (var c in extraContracts)
                registry.Register(c);

        var store = new FileAppendEventStore(eventStorePath);
        await store.InitializeAsync(ct).ConfigureAwait(false);

        var sinks = new List<IObservabilitySink> { new PersistentEventSink(store), new HotRingSink(2000) };
        if (extraSinks is not null) sinks.AddRange(extraSinks);
        var bus = new ObservabilityBus(registry, sinks);

        if (configureObs) Obs.Configure(bus);

        return new StatisticalObservability(bus, store);
    }

    /// <summary>读取嵌入的统计语义契约 JSON。</summary>
    public static string ReadEmbeddedSemantics()
    {
        var asm = typeof(StatisticalObservability).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("statistical_semantics.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("程序集缺少嵌入资源 statistical_semantics.json");
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"无法打开嵌入资源 {name}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>优雅关闭：先显式排空（flush 失败抛出，调用方决定处置），再释放事件区。</summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (Bus is ObservabilityBus concrete)
            await concrete.ShutdownAsync(ct).ConfigureAwait(false); // 先 flush 再释放
        else
            await Bus.FlushAsync(ct).ConfigureAwait(false);
        await Store.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}