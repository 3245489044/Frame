namespace DataCenter.Abstractions.Cells;

/// <summary>
/// 数据单元工厂（端口）—— 由父单元派生作用域分区子单元（scope 分区语义：子单元本地写 + 父链读回退）。
/// </summary>
public interface IDataCellFactory
{
    /// <summary>创建子单元（<paramref name="parent"/> 仅参与读回退，不复制其内容）。</summary>
    IDataCell CreateChild(string name, IDataCell parent);
}
