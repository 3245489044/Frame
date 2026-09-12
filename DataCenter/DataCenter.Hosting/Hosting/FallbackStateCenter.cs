using DataCenter.Abstractions.Cells;
using Operation.StateBlock.Core;
using TConfig.ContextFrame.Abstractions;

namespace DataCenter.Hosting;

/// <summary>
/// 无根宿主回落真源（<b>显式命名</b>，非静默造根）—— 「宿主尚未装配 DataCenter 根」的既有路径
/// （无根宿主 / 单测夹具）经此取得进程本地状态中心。
/// <para>
/// 生产路径应在装配期经 <c>root.Slots.Partition(&lt;domain&gt;)</c> 注册真实状态真源
/// （键 <c>state-center</c>），届时本工厂即被绕过（回落不参与换代迁移，仅迁移期兼容）。
/// </para>
/// <para>
/// 落点理由（K9 单根不变量）：<c>new StateCenter(</c> 属「根的创建与持有」，只允许出现在<b>宿主装配层</b>
/// （本区 / 白名单适配层）；产品侧兜底造根（<c>?? new StateCenter(...)</c>）一律违规
/// ——「取不到根」是装配错误（见 <c>RootMissingException</c>），不给静默造第二根的余地。
/// </para>
/// <para>
/// 存储后端不在此绑定：单元格经 <paramref name="legacyCellFactory"/> 由调用方提供，保持既有后端语义不变。
/// </para>
/// </summary>
public static class FallbackStateCenter
{
    /// <summary>创建回落状态中心（延迟构造引擎存储单元格；null 工厂 → fail-fast）。</summary>
    /// <param name="legacyCellFactory">引擎存储单元格工厂（迁移期兼容路径；实现由调用方决定）。</param>
    /// <param name="partitionCellFactory">
    /// scope 分区载体工厂（F-35 方案 A，可选）—— legacy 后端应传
    /// <c>DataCenter.Adapters.StorageCellPartitionFactory</c> 使 scope 分区落引擎会话子单元；
    /// null = 端口内存工厂（迁移期默认，行为不变）。本层不默认构造引擎工厂：引擎实现程序集不进宿主装配层闭包。
    /// </param>
    public static StateCenter Create(Func<IStorageCell> legacyCellFactory, IDataCellFactory? partitionCellFactory = null)
    {
        ArgumentNullException.ThrowIfNull(legacyCellFactory);
        var cell = legacyCellFactory();
        return partitionCellFactory is null
            ? new StateCenter(cell)
            : new StateCenter(cell, partitionCellFactory);
    }
}
