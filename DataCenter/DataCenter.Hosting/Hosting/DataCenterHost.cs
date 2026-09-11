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

    private DataCenterRoot _root;
    private IStateGeneration _generation;
    private int _shutdown;

    private DataCenterHost(
        DataCenterHostOptions options,
        IDataCell cell,
        StateCenter center,
        HostRuntimeSlot slots,
        HostRootProvider provider,
        DataCenterRoot root,
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
            var projector = options.Projector
                ?? new TierStateProjector(new StateCenterEntrySource(center), options.Registry, options.ValueCodec, options.Generation);

            // 单根守卫在此隐式生效：已存在存活根 → 构造抛错（不产生第二根）
            var root = new DataCenterRoot(cell, projector, options.Registry, options.Generation, slots);

            var provider = new HostRootProvider();
            provider.Bind(root);

            return new DataCenterHost(options, cell, center, slots, provider, root, options.Generation);
        }
        catch
        {
            center?.Shutdown();
            slots.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 换代交换（A0 = 同 ALC 内代际推进；A5 接入新 ALC 装配）：
    /// Drain → 释放旧代数根 → 装配新代数根（generation+1）→ 交换取根指向 → Resume。
    /// 状态真源（cell/center）跨代数复用，故同 scope 值天然连续（"带着运行数据换代"）。
    /// 失败语义：新代数装配失败 → 回建旧代数根（不半迁）并把原异常抛出。
    /// </summary>
    public async Task<IDataCenterRoot> SwapAsync(GenerationPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfShutdown();

        var previousRoot = _root;
        var previousGeneration = _generation;

        if (plan.Drain is not null)
            await plan.Drain(ct).ConfigureAwait(false);

        var nextGeneration = Advance(previousGeneration, plan);

        // 单根守卫不允许两根并存：先解绑取根面（避免读到半迁移根）并释放旧代数，再装配新代数
        _provider.Unbind();
        previousRoot.Dispose();

        try
        {
            _root = BuildRoot(nextGeneration);
        }
        catch
        {
            // 回建旧代数（尽力恢复，保证"换代失败保留旧代数"语义）
            _root = BuildRoot(previousGeneration);
            _provider.Bind(_root);
            throw;
        }

        _generation = nextGeneration;
        _provider.Bind(_root);

        if (plan.Resume is not null)
            await plan.Resume(ct).ConfigureAwait(false);

        return _root;
    }

    /// <summary>
    /// 停机（幂等）：先停写（运行时槽处置：域对象/任务池/网关）→ 再关 Actor 与存储编排 → 最后释放根（守卫配平）。
    /// </summary>
    public Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
            return Task.CompletedTask;

        _provider.Unbind();
        _slots.Dispose();
        _center.Shutdown();
        _root.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>同步释放（宿主退出路径；阻塞等待）。</summary>
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        ShutdownAsync();
        return ValueTask.CompletedTask;
    }

    private DataCenterRoot BuildRoot(IStateGeneration generation)
    {
        var projector = _projectorOverride
            ?? new TierStateProjector(new StateCenterEntrySource(_center), _registry, _codec, generation);
        return new DataCenterRoot(_cell, projector, _registry, generation, _slots);
    }

    private static GenerationState Advance(IStateGeneration current, GenerationPlan plan)
    {
        var state = current as GenerationState
            ?? new GenerationState(current.Generation, current.Fingerprint, current.SchemaVersion);
        var next = state.Next(plan.Fingerprint);
        return plan.SchemaVersion is { } schemaVersion ? next.WithSchemaVersion(schemaVersion) : next;
    }

    private void ThrowIfShutdown()
    {
        if (Volatile.Read(ref _shutdown) != 0)
            throw new ObjectDisposedException(nameof(DataCenterHost));
    }
}
