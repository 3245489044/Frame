namespace DataCenter.Abstractions.Cells;

/// <summary>
/// 分层标识 —— 状态数据的三段默认分区（Config 持久 / Session 跨轮 / Transient 作用域内瞬态）。
/// 该三段为「默认集」而非封闭域：域经 <c>IStateKeyScheme</c> 自备命名空间名，线上表示（CSG tier）为注册制。
/// </summary>
public enum DataTier
{
    Config = 0,
    Session = 1,
    Transient = 2,
}

/// <summary>数据变更类型（Added/Updated/Removed/Cleared）。</summary>
public enum DataChangeKind
{
    Added,
    Updated,
    Removed,
    Cleared,
}

/// <summary>数据单元变更事件参数（端口自有值类型，不依赖任何引擎设施的事件参数）。</summary>
public sealed class DataCellChangedEventArgs : EventArgs
{
    public DataCellChangedEventArgs(string key, object? oldValue, object? newValue, DataChangeKind changeKind, DataTier tier)
    {
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        ChangeKind = changeKind;
        Tier = tier;
    }

    public string Key { get; }

    public object? OldValue { get; }

    public object? NewValue { get; }

    public DataChangeKind ChangeKind { get; }

    public DataTier Tier { get; }
}
