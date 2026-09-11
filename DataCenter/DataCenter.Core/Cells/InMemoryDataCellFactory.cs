using DataCenter.Abstractions.Cells;

namespace Operation.StateBlock.Core;

/// <summary>
/// 内存数据单元工厂 —— scope 分区子单元由父单元派生（子单元本地写 + 父链读回退）。
/// 兼容后端同样适用：父单元为 StorageCellAdapter 时，子单元读回退仍落在同一引擎存储链上。
/// </summary>
public sealed class InMemoryDataCellFactory : IDataCellFactory
{
    public IDataCell CreateChild(string name, IDataCell parent) => new InMemoryDataCell(name, parent);
}
