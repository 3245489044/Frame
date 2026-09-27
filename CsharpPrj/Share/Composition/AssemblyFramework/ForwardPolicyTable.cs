using System;
using System.Collections.Generic;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 转发策略表：显式启停 + 只读快照（实例持有，替代静态可变字典）。
    /// <para>通用件：零领域类型（键与投影值由调用方给定）；<see cref="EnabledKeys"/> 为启用面只读快照，
    /// 供诊断/装配断言消费，不暴露可写集合。</para>
    /// </summary>
    /// <typeparam name="TProjector">投影值类型（通常为载荷投影委托）。</typeparam>
    public sealed class ForwardPolicyTable<TProjector>
    {
        private readonly Dictionary<string, TProjector> _entries;
        private readonly object _gate = new();

        /// <param name="comparer">键比较器（默认 <see cref="StringComparer.Ordinal"/>）。</param>
        public ForwardPolicyTable(IEqualityComparer<string>? comparer = null)
            => _entries = new Dictionary<string, TProjector>(comparer ?? StringComparer.Ordinal);

        /// <summary>开启某键（幂等覆盖式）。</summary>
        public void Enable(string key, TProjector projector)
        {
            ArgumentNullException.ThrowIfNull(projector);
            lock (_gate) _entries[key] = projector;
        }

        /// <summary>关闭某键（幂等）。</summary>
        public void Disable(string key)
        {
            lock (_gate) _entries.Remove(key);
        }

        /// <summary>取投影值（未启用 → false）。</summary>
        public bool TryGet(string key, out TProjector projector)
        {
            lock (_gate) return _entries.TryGetValue(key, out projector!);
        }

        /// <summary>是否启用（不取投影值，供前置判定）。</summary>
        public bool Contains(string key)
        {
            lock (_gate) return _entries.ContainsKey(key);
        }

        /// <summary>启用键只读快照（诊断/装配断言用）。</summary>
        public IReadOnlyList<string> EnabledKeys
        {
            get { lock (_gate) return new List<string>(_entries.Keys); }
        }
    }
}
