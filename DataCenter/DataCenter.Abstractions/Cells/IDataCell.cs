using System.Diagnostics.CodeAnalysis;

namespace DataCenter.Abstractions.Cells;

/// <summary>
/// 分层数据单元（端口）—— 数据中心的最小存储原语：分层键值 + 父链回退 + 变更通知。
/// 默认实现：<c>InMemoryDataCell</c>（DataCenter.Core，零依赖）；引擎设施实现：<c>StorageCellAdapter</c>（DataCenter.Adapters）。
/// 语义契约：Set 触发 <see cref="DataChanged"/>；SetQuiet 持久化但不触发（作用域内瞬态写路径）；
/// 父链仅参与 <see cref="Get{T}"/> / <see cref="TryGet{T}"/> 的值回退，<see cref="Contains"/> / <see cref="Remove"/> 只作用于本层。
/// </summary>
public interface IDataCell
{
    /// <summary>单元名（诊断用）。</summary>
    string Name { get; }

    /// <summary>父单元（读回退链；null = 链尾）。</summary>
    IDataCell? Parent { get; set; }

    /// <summary>写值并触发变更通知。</summary>
    void Set<T>(string key, T value, DataTier tier = DataTier.Session);

    /// <summary>写对象值（运行时类型不定；等价 Set&lt;object?&gt;）。</summary>
    void SetObject(string key, object? value, DataTier tier);

    /// <summary>静默写：持久化值但不触发变更通知（作用域内瞬态写路径）。</summary>
    void SetQuiet<T>(string key, T value, DataTier tier);

    /// <summary>读值；tier=null 时按 tier 顺序回退并上溯父链。</summary>
    T? Get<T>(string key, DataTier? tier = null);

    /// <summary>尝试读值；语义同 <see cref="Get{T}"/>。</summary>
    bool TryGet<T>(string key, [MaybeNullWhen(false)] out T? value, DataTier? tier = null);

    /// <summary>本层存在性探测（不回溯父链；与 <see cref="Get{T}"/> 的父链值回退不同语义），F-33 契约对齐。</summary>
    bool Contains(string key, DataTier? tier = null);

    /// <summary>本层移除（不回溯父链定位：避免 scope 子单元误删父分区键）；返回是否移除成功。</summary>
    bool Remove(string key, DataTier? tier = null);

    /// <summary>清空指定层（逐键通知 Removed）。</summary>
    void ClearTier(DataTier tier);

    /// <summary>枚举指定层的裸键。</summary>
    IEnumerable<string> GetKeys(DataTier tier);

    /// <summary>枚举指定层的键值对。</summary>
    IEnumerable<KeyValuePair<string, object?>> GetEntries(DataTier tier);

    /// <summary>变更通知（单物理事件源）。</summary>
    event EventHandler<DataCellChangedEventArgs>? DataChanged;
}
