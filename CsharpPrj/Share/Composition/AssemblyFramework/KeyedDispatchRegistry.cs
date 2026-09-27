using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 键控分发注册表（注册序保序）：<see cref="Keys"/> 即注册序，<see cref="Allowed"/> 由其派生 ——
    /// 消除「分支集 ∪ 手工白名单」双源手工同步。
    /// <para>通用件：零领域类型（键、handler、连接符与大小写口径均由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="TContext">handler 上下文类型。</typeparam>
    public sealed class KeyedDispatchRegistry<TContext>
    {
        private readonly Dictionary<string, Func<TContext, Task<string>>> _handlers;
        private readonly List<string> _order = new();
        private readonly string _allowedSeparator;

        /// <param name="comparer">查表比较器（默认忽略大小写）。</param>
        /// <param name="allowedSeparator">白名单派生串连接符（默认 <c>" | "</c>）。</param>
        public KeyedDispatchRegistry(IEqualityComparer<string>? comparer = null, string allowedSeparator = " | ")
        {
            _handlers = new Dictionary<string, Func<TContext, Task<string>>>(
                comparer ?? StringComparer.OrdinalIgnoreCase);
            _allowedSeparator = allowedSeparator;
        }

        /// <summary>注册一个键（注册顺序即白名单顺序；同键重复注册 = 覆盖 handler 并再登记一个序位）。</summary>
        public void Register(string key, Func<TContext, Task<string>> handler)
        {
            _handlers[key] = handler;
            _order.Add(key);
        }

        /// <summary>解析键（调用方已归一）；命中 → handler。</summary>
        public bool TryResolve(string key, out Func<TContext, Task<string>> handler)
            => _handlers.TryGetValue(key, out handler!);

        /// <summary>已注册键清单（装配对账 / 测试用；注册序）。</summary>
        public IReadOnlyList<string> Keys => _order;

        /// <summary>允许串（诊断/描述文案单一来源；由注册表派生）。</summary>
        public string Allowed => string.Join(_allowedSeparator, _order);
    }
}
