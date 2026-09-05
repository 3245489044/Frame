using System;
using System.Threading;

namespace Modules.Threading
{
    /// <summary>
    /// 显式命名、可嵌套的 ambient 槽（AsyncLocal 的统一用法容器）。
    /// 纪律：T 应为**只读/不可变快照**（如 RunContext 只读句柄），禁放可写状态句柄，
    /// 以免隐式上下文沿逻辑执行链泄漏可写引用造成双写（对齐 StateBus 治理 P3）。
    /// 用法：Enter 写入并返回句柄，Dispose 恢复先前值（支持嵌套）；后台 worker 入口配合 <see cref="FlowBoundary"/>。
    /// </summary>
    public sealed class AmbientSlot<T> where T : class
    {
        private readonly AsyncLocal<T?> _local;

        public AmbientSlot(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
            _local = new AsyncLocal<T?>();
        }

        public string Name { get; }

        /// <summary>当前逻辑执行链上的值；worker 边界未被显式传入/切断时为 null。</summary>
        public T? Current => _local.Value;

        /// <summary>进入作用域（写入值）；返回句柄，Dispose 恢复先前值。支持嵌套。</summary>
        public IDisposable Enter(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _local.Value;
            _local.Value = value;
            return new Restore(_local, previous);
        }

        /// <summary>清空当前逻辑链上的值。</summary>
        public void Clear() => _local.Value = null;

        private sealed class Restore : IDisposable
        {
            private readonly AsyncLocal<T?> _local;
            private readonly T? _previous;
            private bool _disposed;

            public Restore(AsyncLocal<T?> local, T? previous)
            {
                _local = local;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _local.Value = _previous;
            }
        }
    }
}
