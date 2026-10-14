using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 槽位只读面（非泛型最小集）——供装配态快照统一遍历，不暴露值类型。
    /// <para>通用件：零领域类型。</para>
    /// </summary>
    public interface ISlotRef
    {
        /// <summary>槽键（装配态对账/诊断的唯一标识）。</summary>
        string Key { get; }

        /// <summary>是否已装配。</summary>
        bool Has { get; }
    }

    /// <summary>
    /// 槽生命周期钩子（装配校验与联动）——把「单图 owner 校验 / 订阅镜像 / 接线刷新发布器 / 退订」等
    /// 过程逻辑从宿主 <c>AttachXxx</c> 方法体内联抽为声明式钩子。
    /// <para>通用件：零领域类型（钩子由调用方注入；钩子在持有宿主锁内执行，可重入）。</para>
    /// </summary>
    public interface ISlotLifecycle
    {
        /// <summary>置值前校验（可抛；<paramref name="previous"/> 为旧值，可空）。</summary>
        void OnAttaching(object? previous, object current);

        /// <summary>置值后联动（订阅 / 接线）。</summary>
        void OnAttached(object current);

        /// <summary>解绑前退订（<paramref name="previous"/> 为被解除的值）。</summary>
        void OnDetached(object? previous);
    }

    /// <summary>
    /// 类型化槽引用 —— 承载解析（可随换代迁移） + 生命周期钩子；<see cref="Value"/> / <see cref="Has"/> /
    /// <see cref="Attach"/> / <see cref="Detach"/> / <see cref="Ensure"/> / <see cref="Require"/> 单点实现，
    /// 消除静态宿主逐槽手写的锁样板与 <c>Has*</c> 布尔。
    /// <para>与 <see cref="PartitionedSlot{T}"/> 同面（Value/Has/Attach/Detach/Ensure），另加键、生命周期钩子与
    /// <see cref="Require"/>；与 <see cref="Share.HostAssembly.Concurrency.ThreadSafeSlot{T}"/> 承载不同
    /// （后者持值于进程内存，本类持值于承载解析器所指向的持有点）。</para>
    /// <para>通用件：零领域类型（承载与钩子由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="T">槽值类型（引用类型）。</typeparam>
    public sealed class SlotRef<T> : ISlotRef where T : class
    {
        private readonly object _gate;
        private readonly Func<SlotHolder<T>> _carrier;
        private readonly ISlotLifecycle? _lifecycle;

        /// <param name="gate">跨槽共享锁（调用方持有，保持并发语义一致）。</param>
        /// <param name="key">槽键（装配态对账/诊断）。</param>
        /// <param name="carrier">承载解析（调用方闭包：绑定槽键与解析缝）；须保证同键返回同实例 holder。</param>
        /// <param name="lifecycle">装配生命周期钩子（null = 无钩子）。</param>
        public SlotRef(object gate, string key, Func<SlotHolder<T>> carrier, ISlotLifecycle? lifecycle = null)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            _carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            _lifecycle = lifecycle;
        }

        /// <inheritdoc />
        public string Key { get; }

        private SlotHolder<T> Holder => _carrier();

        /// <summary>当前值；未装配为 null。</summary>
        public T? Value { get { lock (_gate) return Holder.Value; } }

        /// <summary>是否已装配。</summary>
        public bool Has { get { lock (_gate) return Holder.Value != null; } }

        /// <summary>装配（幂等覆盖式）：钩子序 OnAttaching（校验，可抛）→ 置值 → OnAttached（联动）。</summary>
        public void Attach(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                var holder = Holder;
                _lifecycle?.OnAttaching(holder.Value, value);
                holder.Value = value;
                _lifecycle?.OnAttached(value);
            }
        }

        /// <summary>取或惰性创建槽值（聚合承载用）。</summary>
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

        /// <summary>取当前值；未装配则抛出可读异常（供必须运行态的调用方使用）。</summary>
        public T Require(string message)
            => Value ?? throw new InvalidOperationException(message);

        /// <summary>解除装配：钩子 OnDetached（退订）→ 清空（幂等；未装配为 no-op）。</summary>
        public void Detach()
        {
            lock (_gate)
            {
                var holder = Holder;
                var previous = holder.Value;
                if (previous is null) return;
                _lifecycle?.OnDetached(previous);
                holder.Value = null;
            }
        }
    }
}
