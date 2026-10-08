using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 根持有器（VM-P3）—— **默认 ALC 常驻**的进程级单例：持有唯一 <see cref="DataCenterHost"/>，
/// 使「根 / 状态真源 / 单根守卫」跨扩展代数唯一（换代只换扩展装配产物，不换根宿主）。
/// </summary>
/// <remarks>
/// 动机（结构事实）：扩展实现域（如 <c>Agent.*</c>）由可回收 scoped ALC 承载、换代即卸载重建；
/// 若根宿主实例由扩展侧静态槽持有，换代会连同根一起被换掉（根可换 ⇒ 数据连续性失去承载）。
/// 本类落 <c>DataCenter.Hosting</c>（默认 ALC，见 <see cref="RootSwapContext"/> 的 ALC 边界纪律），
/// 扩展侧每代经 <see cref="Current"/> 取同一实例 ⇒ 根宿主不随代换。
/// <para>
/// 纪律：① 进程级唯一（静态单例经 <see cref="Lazy{T}"/> 发布，禁第二持有器）；
/// ② 换代装配入口**每代重设**（<see cref="ConfigureRootFactoryProvider"/>）—— 禁长期持有旧代数 ALC 的委托；
/// ③ Boot / Swap / Shutdown 全程经同一互斥串行化。
/// </para>
/// </remarks>
public sealed class DataCenterHostHolder
{
    private static readonly Lazy<DataCenterHostHolder> Instance =
        new(static () => new DataCenterHostHolder(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>进程级单例（默认 ALC 常驻；跨扩展代数复用同一持有器）。</summary>
    public static DataCenterHostHolder Current => Instance.Value;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DataCenterHost? _host;

    private DataCenterHostHolder()
    {
    }

    /// <summary>是否已引导（根宿主已装配）。</summary>
    public bool IsBooted => Volatile.Read(ref _host) is not null;

    /// <summary>当前根宿主（未引导 = null）。</summary>
    public DataCenterHost? Host => Volatile.Read(ref _host);

    /// <summary>
    /// 幂等引导：未引导 → 经 <paramref name="optionsFactory"/> 装配并持有；已引导 → 复用既有宿主
    /// （跨代唯一：扩展侧每代调用得到的都是同一实例）。选项工厂仅在首建时求值。
    /// </summary>
    public DataCenterHost EnsureBooted(Func<DataCenterHostOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        _gate.Wait();
        try
        {
            var existing = Volatile.Read(ref _host);
            if (existing is not null) return existing;

            var host = DataCenterHost.Boot(optionsFactory());
            Volatile.Write(ref _host, host);
            return host;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>登记**当代**新代数装配入口（每次换代前调用；null = 清除，回落宿主自身选项的入口）。未引导 = no-op。</summary>
    public void ConfigureRootFactoryProvider(Func<RootSwapContext, IDataCenterRoot>? provider)
    {
        var host = Host;
        if (host is not null) host.RootFactoryProvider = provider;
    }

    /// <summary>换代（未引导 ⇒ fail-fast）；经所持宿主的 <see cref="DataCenterHost.SwapAsync"/> 执行。</summary>
    public Task<IDataCenterRoot> SwapAsync(GenerationPlan? plan = null, CancellationToken ct = default)
    {
        var host = Host ?? throw new InvalidOperationException(
            "未引导根：换代须先经 EnsureBooted 引导（未引导不产生可换代的根）。");
        return host.SwapAsync(plan ?? new GenerationPlan(), ct);
    }

    /// <summary>停机释放根（幂等；释放后允许再次引导）。</summary>
    public void Shutdown()
    {
        _gate.Wait();
        try
        {
            var host = Interlocked.Exchange(ref _host, null);
            host?.ShutdownAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>测试隔离：清空持有登记（**不释放宿主**；供用例自行处置以避免跨用例串味）。</summary>
    public void ResetForTesting() => Volatile.Write(ref _host, null);
}
