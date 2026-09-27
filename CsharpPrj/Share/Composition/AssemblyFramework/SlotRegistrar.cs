using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 槽分区访问面（由域侧适配 <c>IRuntimeSlotPartition</c> 等具体承载实现；框架不依赖具体承载）。
    /// </summary>
    public interface ISlotPartition
    {
        /// <summary>尝试解析槽对象（类型不符 = 未命中，不抛错）。</summary>
        bool TryGet<T>(string key, out T? value) where T : class;

        /// <summary>注册槽对象（装配期单点写入）。</summary>
        void Register(string key, object value);

        /// <summary>替换槽对象（处置期写口；同实例幂等）。</summary>
        void Replace(string key, object value);
    }

    /// <summary>异实例替换留痕载荷（域侧接诊断日志）。</summary>
    /// <param name="Key">槽键。</param>
    /// <param name="Previous">替换前实例类型。</param>
    /// <param name="Current">替换后实例类型。</param>
    public readonly record struct SlotOverride(string Key, Type Previous, Type Current);

    /// <summary>
    /// 槽登记语义：缺键 <c>Register</c> / 同实例幂等 / 异实例 <c>onOverride</c> 留痕后 <c>Replace</c>。
    /// <para>通用件：零领域类型（留痕动作由调用方以委托注入）。</para>
    /// </summary>
    public sealed class SlotRegistrar
    {
        private readonly ISlotPartition _partition;
        private readonly Action<SlotOverride>? _onOverride;

        /// <param name="partition">槽分区访问面。</param>
        /// <param name="onOverride">异实例替换留痕回调（null = 不留痕）。</param>
        public SlotRegistrar(ISlotPartition partition, Action<SlotOverride>? onOverride = null)
        {
            _partition = partition ?? throw new ArgumentNullException(nameof(partition));
            _onOverride = onOverride;
        }

        /// <summary>登记槽值（<c>null</c> 值 → no-op）。</summary>
        public void Register(string key, object? value)
        {
            if (value is null) return;

            if (!_partition.TryGet<object>(key, out var existing))
            {
                _partition.Register(key, value);
                return;
            }

            if (ReferenceEquals(existing, value)) return;

            _onOverride?.Invoke(new SlotOverride(key, existing.GetType(), value.GetType()));
            _partition.Replace(key, value);
        }
    }
}
