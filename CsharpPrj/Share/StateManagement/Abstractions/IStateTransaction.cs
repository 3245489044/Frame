namespace Share.StateManagement
{
    /// <summary>
    /// 原子事务：一次 Load/Destroy = 一个事务。事务体内所有 Set/Remove 先写事务缓冲，
    /// Commit 时按序落到 IStorageCell，DataChanged 合并为一次发布。
    /// 中间态（Loading/半挂载）对订阅者不可见——这是 L4 竞态的根治点。
    /// </summary>
    public interface IStateTransaction : IDisposable
    {
        string? Scope { get; }

        void Set<TState>(StateKey key, TState value);

        void Remove(StateKey key);

        /// <summary>批量写入 + 合并事件为一次发布；未 Commit 前中间态不外泄。</summary>
        void Commit();

        /// <summary>丢弃所有未提交变更，不产生任何事件。</summary>
        void Rollback();
    }
}
