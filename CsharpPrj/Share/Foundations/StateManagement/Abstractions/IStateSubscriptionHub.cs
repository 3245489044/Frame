namespace Share.StateManagement
{
    /// <summary>
    /// 订阅中枢：事务提交后由 StateCenter 调用的统一发布入口（投影器唯一数据源）。
    /// 每 scope 维护版本表，版本 ≤ lastSeen 的事件丢弃（版本去重）。
    /// </summary>
    public interface IStateSubscriptionHub : IDisposable
    {
        /// <summary>订阅指定命名空间+作用域的状态变更；scope 为空 = 该命名空间全量。</summary>
        IDisposable Subscribe(string stateNamespace, string? scope, Action<StateKey, StateSnapshot<object?>> handler);

        /// <summary>事务提交后由 StateCenter 调用的统一发布入口（投影器唯一数据源）。</summary>
        void Publish(StateKey key, object? value, string? source = null);

        /// <summary>显式版本发布（版本冲突时自动提升为单调递增，不丢发布）。</summary>
        void Publish(StateKey key, object? value, long version, string? source = null);

        /// <summary>内部：为每个 scope 维护版本表，版本 ≤ lastSeen 的事件丢弃。</summary>
        long GetLastVersion(string stateNamespace, string scope);
    }
}
