using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement
{
    /// <summary>
    /// 事务缓冲 + Commit/Rollback。一次 Load/Destroy = 一个事务：
    /// Commit 按调用序落 IStorageCell（Session 层）+ 逐 key 经订阅中枢发布（版本单调递增）；
    /// Set/Remove 交错顺序被保留（最后操作胜出，E7）；Dispose 未 Commit 自动 Rollback（不产生任何事件）。
    /// </summary>
    public sealed class StateTransaction : IStateTransaction
    {
        private readonly IStorageCell _cell;
        private readonly IStateSubscriptionHub _hub;
        private readonly List<TransactionOp> _ops = new();
        private bool _finished;

        private readonly record struct TransactionOp(bool IsRemove, StateKey Key, object? Value);

        public string? Scope { get; }

        public StateTransaction(IStorageCell cell, IStateSubscriptionHub hub, string? scope = null)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            Scope = scope;
        }

        public void Set<TState>(StateKey key, TState value)
        {
            ThrowIfFinished();
            _ops.Add(new TransactionOp(false, key, value));
        }

        public void Remove(StateKey key)
        {
            ThrowIfFinished();
            _ops.Add(new TransactionOp(true, key, null));
        }

        public void Commit()
        {
            ThrowIfFinished();

            // 按调用序逐条执行，保留 Set/Remove 交错顺序（最后操作胜出，E7）；
            // 版本由订阅中枢在锁内原子分配（单调递增），避免并发事务版本冲突丢发布（E1）。
            foreach (var op in _ops)
            {
                if (op.IsRemove)
                {
                    _cell.Remove(op.Key.FullKey, StorageLayer.Session);
                    _hub.Publish(op.Key, null);
                }
                else
                {
                    _cell.SetObject(op.Key.FullKey, op.Value, StorageLayer.Session);
                    _hub.Publish(op.Key, op.Value);
                }
            }

            _finished = true;
        }

        public void Rollback()
        {
            _ops.Clear();
            _finished = true;
        }

        public void Dispose()
        {
            if (!_finished) Rollback();
        }

        private void ThrowIfFinished()
        {
            if (_finished)
                throw new InvalidOperationException("事务已提交/回滚，禁止继续写入");
        }
    }
}
