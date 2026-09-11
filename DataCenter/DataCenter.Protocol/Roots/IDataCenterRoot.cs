using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Projection;
using DataCenter.Protocol.Registry;

namespace DataCenter.Protocol.Roots;

/// <summary>
/// 根实例的唯一外部面（DF8：整体数据流由一个根实例持有）—— 只持有与转发，不含任何域语义。
/// 状态真源经 <see cref="Root"/>（端口）暴露；图/注册表/代际均挂在根上（禁止各自静态持有），
/// 否则换代时必然漏迁（权威: 设计文档 §12.3/§12.8、SSOT StateRootSingleSource）。
/// <para>
/// 方法面纪律（R12）：根只做"持有 + 转发 + 装配"；出现"根上的业务方法"即设计错误（域语义属 L4）。
/// </para>
/// <para>
/// 追踪查询面（ITraceQuery）随 D3（追踪面：查询/索引/导出/归档）落地后加入本契约；
/// 代际交换点（ISwapPoint）随 D5（热换代）落地后加入 —— 不预置无实现的空端口。
/// </para>
/// </summary>
public interface IDataCenterRoot : IDisposable
{
    /// <summary>状态真源（唯一状态写入路径；图由 Projector 由此派生，图侧不存在独立写口）。</summary>
    IDataCell Root { get; }

    /// <summary>图投影（图的唯一产出点；只读派生，可重算）。</summary>
    IStateProjector Projector { get; }

    /// <summary>类型/键注册表（只读消费面）。</summary>
    IStateRegistry Registry { get; }

    /// <summary>代际读口（generation/fingerprint/schemaVersion）。</summary>
    IStateGeneration Generation { get; }

    /// <summary>
    /// 运行时槽分区（根的另一半）—— 承载不可序列化的进程内对象（网关/缓存/装配产物）。
    /// 装配期注册、运行期只读；未装配槽的宿主返回只读空分区（不抛错、不接受注册）。
    /// </summary>
    IRuntimeSlot Slots { get; }
}

/// <summary>
/// 取得口 —— 替代静态槽：深层组件（无法构造注入）经本端口取根；<see cref="IRootProvider"/> 本身仍由组合根注入。
/// 取得优先级（权威: 设计文档 §12.4）：构造注入 &gt; IRootProvider 注入 &gt; 宿主装配期服务定位器；
/// <c>static</c> 槽与兜底造根（<c>?? new StateCenter(...)</c>）一律禁止。
/// </summary>
public interface IRootProvider
{
    /// <summary>整体数据流持有者；未装配 = 装配期错误（fail-fast，见 <see cref="RootMissingException"/>）。</summary>
    IDataCenterRoot Root { get; }
}

/// <summary>
/// 缺根异常 —— "取不到根"是<b>装配错误</b>，不是运行时可恢复异常：不给"静默造根"的机会
/// （兜底第二根 = 状态分叉后不可追踪；权威: 设计文档 §12.4 反模式清单）。
/// </summary>
public sealed class RootMissingException : InvalidOperationException
{
    public RootMissingException()
        : base("DataCenter 根实例未装配：请经组合根构造 IDataCenterRoot 并经 IRootProvider 注入（禁止静态槽/兜底造根）。")
    {
    }

    public RootMissingException(string message) : base(message)
    {
    }

    public RootMissingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>缺根 fail-fast 的统一抛点（消费方在装配/调用期显式失败）。</summary>
    public static RootMissingException Throw(string requester)
        => throw new RootMissingException($"组件 {requester} 未取得 DataCenter 根实例（IRootProvider.Root 为 null 或未注入）。");
}
