using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Projection;
using DataCenter.Protocol.Registry;
using DataCenter.Protocol.Roots;

namespace Operation.StateBlock.Roots;

/// <summary>
/// 根实例默认实现（DF8）—— 整体数据流（状态真源 + 投影器 + 注册表 + 代际）由单一实例持有；
/// 全部组件经构造注入（端口），根自身<b>只持有与转发</b>，不含域语义、不新增写口（DF1）。
/// <para>
/// 纪律：进程内 ≤ 1（第二实例 fail-fast）；缺根时由 <see cref="IRootProvider"/> 抛
/// <see cref="RootMissingException"/>，不以 <c>?? new StateCenter(...)</c> 兜底。
/// 权威: 设计文档 §12.2/§12.3/§12.5。
/// </para>
/// </summary>
public sealed class DataCenterRoot : IDataCenterRoot
{
    private readonly IDataCell _root;
    private readonly IStateProjector _projector;
    private readonly IStateRegistry _registry;
    private readonly IStateGeneration _generation;
    private readonly IRuntimeSlot _slots;
    private int _disposed;

    /// <param name="root">状态真源（端口；如 InMemoryDataCell 或 StorageCellAdapter）。</param>
    /// <param name="projector">图投影器（图的唯一产出点）。</param>
    /// <param name="registry">类型/键注册表（装配期注册、运行期只读）。</param>
    /// <param name="generation">代际读口（初始 <see cref="GenerationState.Initial"/>）。</param>
    /// <param name="slots">运行时槽分区（根的另一半）；null = 未装配槽的宿主（只读空分区）。</param>
    public DataCenterRoot(
        IDataCell root,
        IStateProjector projector,
        IStateRegistry registry,
        IStateGeneration generation,
        IRuntimeSlot? slots = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _slots = slots ?? EmptyRuntimeSlot.Instance;

        // 单根：进入即断言（失败则本实例不成立，构造抛错）
        RootInstanceGuard.Enter();
    }

    public IDataCell Root
    {
        get
        {
            ThrowIfDisposed();
            return _root;
        }
    }

    public IStateProjector Projector
    {
        get
        {
            ThrowIfDisposed();
            return _projector;
        }
    }

    public IStateRegistry Registry
    {
        get
        {
            ThrowIfDisposed();
            return _registry;
        }
    }

    public IStateGeneration Generation
    {
        get
        {
            ThrowIfDisposed();
            return _generation;
        }
    }

    /// <summary>运行时槽分区（根的另一半）：装配期由宿主注册、运行期只读。</summary>
    public IRuntimeSlot Slots
    {
        get
        {
            ThrowIfDisposed();
            return _slots;
        }
    }

    /// <summary>
    /// 释放根（Shutdown 单一释放点）。顺序纪律：宿主应先停止写入、再关闭 Actor、最后释放存储 ——
    /// 三者的持有者由宿主装配注入，本类只负责置位与释放单根守卫（不越权释放注入物）。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RootInstanceGuard.Exit();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DataCenterRoot));
    }
}
