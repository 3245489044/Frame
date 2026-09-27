using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 分区承载槽：值与进程内存解耦，寄存于调用方注入的承载（可随换代迁移）；键不可移除时以可变
    /// holder 表达 Detach 语义。与 <see cref="Share.HostAssembly.Concurrency.ThreadSafeSlot{T}"/> 同面
    /// （Value/Has/Attach/Detach）而承载不同：后者持值于进程内存，本类持值于承载解析器所指向的持有点
    /// （如取根分区槽，未绑根时回落进程本地容器）。
    /// <para>通用件：零领域类型（承载解析由调用方以委托注入）。</para>
    /// </summary>
    /// <typeparam name="T">槽值类型（引用类型）。</typeparam>
    public sealed class PartitionedSlot<T> where T : class
    {
        private readonly object _gate;
        private readonly Func<SlotHolder<T>> _carrier;

        /// <param name="gate">跨槽共享锁（调用方持有，保持并发语义一致）。</param>
        /// <param name="carrier">承载解析（调用方闭包：绑定槽键与解析缝）；须保证同键返回同实例 holder。</param>
        public PartitionedSlot(object gate, Func<SlotHolder<T>> carrier)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
        }

        private SlotHolder<T> Holder => _carrier();

        /// <summary>当前值；未装配为 null。</summary>
        public T? Value { get { lock (_gate) return Holder.Value; } }

        /// <summary>是否已装配。</summary>
        public bool Has { get { lock (_gate) return Holder.Value != null; } }

        /// <summary>装配（幂等覆盖式）。传入需非 null。</summary>
        public void Attach(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate) Holder.Value = value;
        }

        /// <summary>取或惰性创建槽值（聚合承载用：槽值 = 视图集合，无 Attach 单值语义）。</summary>
        public T Ensure(Func<T> create)
        {
            ArgumentNullException.ThrowIfNull(create);
            lock (_gate)
            {
                var holder = Holder;
                holder.Value ??= create();
                return holder.Value;
            }
        }

        /// <summary>解除装配（置空；键不可移除的承载契约下 Detach 的唯一表达）。</summary>
        public void Detach() { lock (_gate) Holder.Value = null; }
    }

    /// <summary>槽值 holder（可变；由承载解析保证同键同实例）。</summary>
    public sealed class SlotHolder<T> where T : class
    {
        /// <summary>holder 值（可变；null = 未装配）。</summary>
        public T? Value;
    }
}
