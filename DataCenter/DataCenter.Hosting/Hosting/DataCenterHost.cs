using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Projection;
using DataCenter.Protocol.Registry;
using DataCenter.Protocol.Roots;
using Operation.StateBlock.Core;
using Operation.StateBlock.Generation;
using Operation.StateBlock.Projection;
using Operation.StateBlock.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 进程内单根宿主（DF8 / P10）—— 整体数据流（状态真源 + 投影器 + 注册表 + 代际 + 运行时槽分区）
/// 由本类创建与持有；业务侧只经 <see cref="IRootProvider"/> 取根，不暴露 <c>static Current</c>。
/// <para>
/// 不变量：① 进程内根 ≤ 1（由 <c>DataCenterRoot</c> 的单根守卫保证，本类不另立计数）；
/// ② Boot 失败不残留根（装配回滚）；③ 释放顺序：先停写（槽处置）→ 再关 Actor/存储 → 最后释放根。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-设计与实施.md §2.1/§2.3、§4 A0、§6 D1/D2。
/// </summary>
public sealed class DataCenterHost : IAsyncDisposable
{
    private readonly IDataCell _cell;
    private readonly StateCenter _center;
    private readonly HostRuntimeSlot _slots;
    private readonly HostRootProvider _provider;
    private readonly IStateRegistry _registry;
    private readonly IStateValueCodec _codec;
    private readonly IStateProjector? _projectorOverride;
    private readonly Func<RootSwapContext, IDataCenterRoot>? _rootFactory;

    private IDataCenterRoot _root;
    private IStateGeneration _generation;
    private int _shutdown;

    /// <summary>换代互斥（F-37）：进程内同时至多一次 Swap，且与停机互斥（防交错产生"复活根"）。</summary>
    private readonly SemaphoreSlim _swapGate = new(1, 1);

    private DataCenterHost(
        DataCenterHostOptions options,
        IDataCell cell,
        StateCenter center,
        HostRuntimeSlot slots,
        HostRootProvider provider,
        IDataCenterRoot root,
        IStateGeneration generation)
    {
        _cell = cell;
        _center = center;
        _slots = slots;
        _provider = provider;
        _root = root;
        _generation = generation;
        _registry = options.Registry;
        _codec = options.ValueCodec;
        _projectorOverride = options.Projector;
        _rootFactory = options.RootFactory;
    }

    /// <summary>整体数据流持有者（当前代数根）。</summary>
    public IDataCenterRoot Lifetime
    {
        get
        {
            ThrowIfShutdown();
            return _root;
        }
    }

    /// <summary>业务取根唯一路径（随换代交换指向；禁止外泄 static 槽）。</summary>
    public IRootProvider Root => _provider;

    /// <summary>状态真源（宿主装配层内部使用；业务侧应经域 Gateway 读写）。</summary>
    public StateCenter State
    {
        get
        {
            ThrowIfShutdown();
            return _center;
        }
    }

    /// <summary>运行时槽装配面（装配期只读核对；注册应经 <see cref="DataCenterHostOptions.ConfigureSlots"/>）。</summary>
    public IRuntimeSlot Slots => _slots;

    /// <summary>当前代际（读口）。</summary>
    public IStateGeneration Generation
    {
        get
        {
            ThrowIfShutdown();
            return _generation;
        }
    }

    /// <summary>是否已停机。</summary>
    public bool IsShutdown => Volatile.Read(ref _shutdown) != 0;

    /// <summary>
    /// 新代数装配入口的**每代可重设提供者**（默认 ALC 常驻持有器用）—— 非 null 时优先于
    /// <see cref="DataCenterHostOptions.RootFactory"/>。供持有器在每次换代前重新登记**当代**的装配入口，
    /// 避免默认 ALC 侧长期持有旧扩展代数 ALC 的委托（防跨代句柄钉住旧 ALC）。
    /// </summary>
    public Func<RootSwapContext, IDataCenterRoot>? RootFactoryProvider { get; set; }

    /// <summary>
    /// 装配并启动单根宿主（进程内唯一入口）。
    /// 失败语义：二次 Boot → 单根守卫 fail-fast（<see cref="InvalidOperationException"/>）；
    /// 装配中途失败 → 回滚已建资源（不残留根、不泄漏槽）。
    /// </summary>
    public static DataCenterHost Boot(DataCenterHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var slots = new HostRuntimeSlot();
        StateCenter? center = null;
        try
        {
            // 槽装配先行（域装配产物在根成立前就位；失败即整体回滚）
            options.ConfigureSlots?.Invoke(slots);

            var cell = options.RootCellFactory?.Invoke() ?? new InMemoryDataCell(options.RootCellName);
            center = new StateCenter(cell, options.PartitionCellFactory);
            var projector = CreateProjector(options.Projector, center, options.Registry, options.ValueCodec, options.Generation);

            // 单根守卫在此隐式生效：已存在存活根 → 构造抛错（不产生第二根）
            var root = new DataCenterRoot(cell, projector, options.Registry, options.Generation, slots);

            var provider = new HostRootProvider();
            provider.Bind(root);

            return new DataCenterHost(options, cell, center, slots, provider, root, options.Generation);
        }
        catch (Exception assemblyFailure)
        {
            // 装配回滚（F-34）：释放异常不得覆盖原始装配异常 —— 原始异常优先，释放失败降级为附加信息
            var rollbackFailures = new List<Exception>();
            TryRelease(rollbackFailures, "state-center(rollback)", () => center?.Shutdown());
            TryRelease(rollbackFailures, "runtime-slots(rollback)", slots.Dispose);

            if (rollbackFailures.Count > 0)
                throw new AggregateException(assemblyFailure.Message, [assemblyFailure, .. rollbackFailures]);

            throw;
        }
    }

    /// <summary>
    /// 换代交换（A0 = 同 ALC 内代际推进；A5/S4-2 = 接入宿主注入的新代数装配入口）：
    /// Drain → Export → 释放旧代数根 → 装配新代数根（generation+1）→ 绑根 → Import → Resume（finally 放行写入）。
    /// 状态真源（cell/center）跨代数复用，故同 scope 值与版本序列天然连续（"带着运行数据换代"）。
    /// <para>
    /// 失败语义：① Drain/Export 失败 → 不动旧根（旧代数完好）并照常放行写入；
    /// ② 新代数装配/Import 失败 → 释放新根并<b>回建旧代数根</b>（不半迁）后外抛原异常。
    /// </para>
    /// <para>
    /// Resume 在 <c>finally</c> 中以 <see cref="CancellationToken.None"/> 调用：换代成功/失败/取消都必须放行写入，
    /// 否则宿主写路径将永久停在停写态（D-A5 ①：只承诺边界，不承诺零停机）。
    /// </para>
    /// </summary>
    public async Task<IDataCenterRoot> SwapAsync(GenerationPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfShutdown();

        // F-37：换代全程互斥（并发 Swap 只允许一次装配；后到者拿锁后复检停机态）
        await _swapGate.WaitAsync(ct).ConfigureAwait(false);

        var previousRoot = _root;
        var previousGeneration = _generation;
        Exception? failure = null;

        try
        {
            ThrowIfShutdown();   // 等待互斥期间可能已停机

            // F-37 去重：纯代际推进（无边界/载荷钩子）且内容指纹与当前代际相同 → 无需换代
            // （并发同计划只装配一次；RootFactory 至多调用一次）
            if (plan.Drain is null && plan.Resume is null && plan.Export is null && plan.Import is null &&
                plan.Fingerprint is { Length: > 0 } fingerprint &&
                string.Equals(fingerprint, _generation.Fingerprint, StringComparison.Ordinal))
            {
                return _root;
            }

            if (plan.Drain is not null)
                await plan.Drain(ct).ConfigureAwait(false);

            // F-37：Drain 期间可能已停机 → 不绑新根（避免"复活根"）；旧根处置由停机路径负责
            if (Volatile.Read(ref _shutdown) != 0)
                throw new ObjectDisposedException(nameof(DataCenterHost));

            // Export 在旧代数释放前调用（载荷只承载"不在状态真源里"的跨代迁移数据）
            var payload = plan.Export is null ? null : await plan.Export(ct).ConfigureAwait(false);

            var nextGeneration = Advance(previousGeneration, plan);

            // 单根守卫不允许两根并存：先解绑取根面（避免读到半迁移根）并释放旧代数，再装配新代数
            _provider.Unbind();
            previousRoot.Dispose();

            IDataCenterRoot? nextRoot = null;
            try
            {
                nextRoot = BuildRoot(nextGeneration, previousGeneration);
                _root = nextRoot;
                _generation = nextGeneration;
                _provider.Bind(_root);

                if (plan.Import is not null)
                    await plan.Import(payload ?? GenerationPayload.Empty, ct).ConfigureAwait(false);

                return _root;
            }
            catch (Exception assemblyFailure)
            {
                // 回建旧代数（尽力恢复，保证"换代失败保留旧代数"语义；真源复用 → 值不丢）
                ReleaseRoot(nextRoot);
                _provider.Unbind();

                try
                {
                    // F-21：回建亦经装配入口（RootFactory），与"重建 + 状态迁移"承诺一致（原走核心路径 →
                    // 对象层"保留旧代数"不成立）；目标代际与上一代同为 previousGeneration，避免回建出"新一代"根。
                    _root = BuildRoot(previousGeneration, previousGeneration);
                    _generation = previousGeneration;
                    _provider.Bind(_root);
                }
                catch (Exception rebuildFailure)
                {
                    // F-22：装配入口回建失败 → 显式降级到核心路径（同真源 + 旧代际；对象槽未释放仍在位）；
                    // 降级成功仍抛原始装配异常（回建失败不得掩盖根因）；降级亦失败 → 聚合外抛（原始异常居首）。
                    try
                    {
                        _root = BuildRoot(previousGeneration);
                        _generation = previousGeneration;
                        _provider.Bind(_root);
                    }
                    catch (Exception degradeFailure)
                    {
                        throw new AggregateException(
                            "新代数装配失败、装配入口回建失败且降级回建亦失败（首个内部异常为装配根因）。",
                            assemblyFailure, rebuildFailure, degradeFailure);
                    }
                }

                throw;   // 原始装配异常优先（栈保留；回建/降级均不改写根因）
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            try
            {
                if (plan.Resume is not null)
                {
                    try
                    {
                        await plan.Resume(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception resumeFailure) when (failure is not null)
                    {
                        // 换代根因优先（F-19）：Resume 失败不得覆盖 Drain/Export/装配/Import 的原始异常 ——
                        // 原始异常置于首位（根因可辨），Resume 失败作为附加内部异常保留（不静默丢弃）。
                        throw new AggregateException(
                            "换代失败且 Resume 亦失败（首个内部异常为换代根因）。", failure, resumeFailure);
                    }
                }
            }
            finally
            {
                _swapGate.Release();   // F-37：Resume 是否抛错都必须释放换代互斥（防永久死锁）
            }
        }
    }

    /// <summary>
    /// 停机（幂等）：先停写（运行时槽处置：域对象/任务池/网关）→ 再关 Actor 与存储编排 → 最后释放根（守卫配平）。
    /// <para>
    /// 三段各自隔离（F-34）：任一阶段释放失败都<b>不得阻断</b>后续阶段 —— 尤其 <see cref="_root"/>.Dispose 必达，
    /// 否则单根守卫不归零而 <see cref="_shutdown"/> 已置位 ⇒ 进程内永久无法再 Boot。末端聚合外抛（不静默吞错）。
    /// </para>
    /// </summary>
    public Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
            return Task.CompletedTask;

        // F-37：与换代互斥 —— 停机不得与在途 Swap 交错（否则可能在停机后又绑上新根 = "复活根"）
        _swapGate.Wait();
        try
        {
            var failures = new List<Exception>();
            _provider.Unbind();

            TryRelease(failures, "runtime-slots", _slots.Dispose);
            TryRelease(failures, "state-center", _center.Shutdown);
            TryRelease(failures, "root", _root.Dispose);

            return failures.Count > 0
                ? Task.FromException(new AggregateException("停机存在失败阶段（已尽力释放全部阶段）。", failures))
                : Task.CompletedTask;
        }
        finally
        {
            _swapGate.Release();
        }
    }

    /// <summary>同步释放（宿主退出路径；阻塞等待）。</summary>
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    /// <summary>尽力执行释放动作并收集失败（隔离单段异常，保证后续段必达）。</summary>
    private static void TryRelease(List<Exception> failures, string stage, Action release)
    {
        try
        {
            release();
        }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException($"停机阶段 '{stage}' 释放失败（已继续释放其余阶段）。", ex));
        }
    }

    /// <summary>
    /// 投影器装配单一工厂（F-02/T3）—— 首建（<see cref="Boot"/>）与换代（<see cref="BuildRoot(IStateGeneration)"/>）
    /// 两条路径共用本口径，消除"改一处漏一处"的装配表达式重复。
    /// </summary>
    private static IStateProjector CreateProjector(
        IStateProjector? projectorOverride, StateCenter center, IStateRegistry registry,
        IStateValueCodec codec, IStateGeneration generation)
        => projectorOverride
           ?? new TierStateProjector(new StateCenterEntrySource(center), registry, codec, generation);

    /// <summary>
    /// 核心装配路径根（默认 ALC；复用当前真源/投影器/槽，目标代际为 <paramref name="generation"/>）。
    /// 供宿主在 <see cref="DataCenterHostOptions.RootFactory"/> 内<b>复用同一装配口径</b>（T3/F-02：
    /// 宿主自建 RootFactory 时经此委派，勿在宿主侧重复投影器表达式），以及需要"重建根但扩展产物不变"的换代场景。
    /// 纪律：本方法交出物必挂本宿主的状态真源与槽承载（故必然通过 <see cref="BuildRoot(IStateGeneration, IStateGeneration)"/> 的交出物校验），
    /// 不得用于绕过单根守卫另立第二根。
    /// </summary>
    public IDataCenterRoot CreateCoreRoot(IStateGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ThrowIfShutdown();
        return BuildRoot(generation);
    }

    /// <summary>核心装配路径（默认 ALC + 复用真源/投影器）—— 未注入装配入口时的唯一路径。</summary>
    private DataCenterRoot BuildRoot(IStateGeneration generation)
    {
        // F-29：投影器构造单点（原实例版重载仅透传，已收敛）
        var projector = CreateProjector(_projectorOverride, _center, _registry, _codec, generation);
        return new DataCenterRoot(_cell, projector, _registry, generation, _slots);
    }

    /// <summary>
    /// 装配新代数根：注入装配入口时经其装配（扩展装配产物在新 ALC 内重建），否则走核心路径。
    /// 交出物校验（fail-fast，不静默降级）：① 不得为 null（避免"以为换了其实没换"）；
    /// ② 必须挂当前状态真源（否则换代 = 换空真源，运行数据丢失、版本序列重置）；③ 代际必须为目标代数。
    /// 校验不通过先释放交出物再抛错：根构造已占用单根守卫，泄漏会使"回建旧代数"二次占用而失败。
    /// </summary>
    private IDataCenterRoot BuildRoot(IStateGeneration generation, IStateGeneration previousGeneration)
    {
        var factory = RootFactoryProvider ?? _rootFactory;
        if (factory is null)
            return BuildRoot(generation);

        var root = factory(new RootSwapContext(_cell, _registry, generation, previousGeneration, _slots));
        if (root is null)
            throw new InvalidOperationException(
                "RootFactory 返回 null：新代数装配入口必须交出根实例（不静默回退同 ALC 路径）。");

        try
        {
            if (!ReferenceEquals(root.Root, _cell))
                throw new InvalidOperationException(
                    "RootFactory 交出的新代数根未挂当前状态真源（DataCenterHost 的 cell）：换代必须复用真源以保持状态连续（S4-2）。");

            if (root.Generation.Generation != generation.Generation)
                throw new InvalidOperationException(
                    $"RootFactory 交出的新代数根代际不符（期望 {generation.Generation}，实得 {root.Generation.Generation}）：请使用上下文 Generation 装配。");
        }
        catch
        {
            ReleaseRoot(root);
            throw;
        }

        return root;
    }

    /// <summary>尽力释放根（失败不掩盖原始换代异常）。</summary>
    private static void ReleaseRoot(IDataCenterRoot? root)
    {
        if (root is null)
            return;

        try
        {
            root.Dispose();
        }
        catch
        {
            // 尽力释放：释放失败不覆盖原始换代异常（根由单根守卫配平，泄漏面优于异常丢失）
        }
    }

    private static GenerationState Advance(IStateGeneration current, GenerationPlan plan)
    {
        var state = current as GenerationState
            ?? new GenerationState(current.Generation, current.Fingerprint, current.SchemaVersion);
        // 未提供指纹 → 沿用既有（不得静默置空）：世代指纹可回溯（B2/F-18，AX-CORE.InformationMonotonicity）
        var next = state.Next(plan.Fingerprint ?? state.Fingerprint);
        return plan.SchemaVersion is { } schemaVersion ? next.WithSchemaVersion(schemaVersion) : next;
    }

    private void ThrowIfShutdown()
    {
        if (Volatile.Read(ref _shutdown) != 0)
            throw new ObjectDisposedException(nameof(DataCenterHost));
    }
}
