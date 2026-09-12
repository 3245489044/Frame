using DataCenter.Abstractions.Cells;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Extensions;
using Operation.StateBlock.Core;
using Operation.StateBlock.Core.Compat;
using TConfig.ContextFrame.Abstractions;

namespace DataCenter.Adapters;

/// <summary>
/// scope 分区载体工厂（F-35 方案 A）—— <b>按后端选择</b>分区载体：
/// <list type="number">
/// <item>legacy 后端（引擎 <see cref="StorageCell"/>）→ 经 <c>CreateChildCell</c> 派生<b>引擎会话子单元</b>再适配为端口
///   （scope 状态保留引擎会话/快照语义，不再落入与引擎无关的内存子单元）；</item>
/// <item>端口后端（如 <c>DataCellStorageView</c> 等非引擎单元）→ 回落 <see cref="InMemoryDataCellFactory"/>
///   （端口后端行为不变）。</item>
/// </list>
/// <para>
/// 落点理由（G2）：引擎实现程序集只在本适配层出现；Core 经此端口注入获得引擎子单元，自身不依赖引擎实现。
/// </para>
/// </summary>
public sealed class StorageCellPartitionFactory : IDataCellFactory
{
    private readonly IStorageCell _root;
    private readonly IDataCellFactory _portFallback = new InMemoryDataCellFactory();

    /// <param name="root">状态中心的根存储单元（legacy 后端为引擎单元；端口后端为契约视图）。</param>
    public StorageCellPartitionFactory(IStorageCell root)
        => _root = root ?? throw new ArgumentNullException(nameof(root));

    public IDataCell CreateChild(string name, IDataCell parent)
        => _root is StorageCell engineRoot
            ? new StorageCellAdapter(engineRoot.CreateChildCell(name))
            : _portFallback.CreateChild(name, parent);
}