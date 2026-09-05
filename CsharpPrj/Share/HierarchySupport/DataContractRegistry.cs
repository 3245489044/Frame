using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 数据契约注册表 —— 统一数据传递标准的内容仓库（进程级单一事实源）。
    /// 契约由装配层经 <see cref="Register"/> 播种（Agent.Axioms 以 RoundKeys + 强类型播种），运行时只读；
    /// 数据空间写前校验必须已注册，杜绝未声明键静默流通（魔法键 / ActiveStrategy 只写不读类断边在源头暴露）。
    /// </summary>
    public static class DataContractRegistry
    {
        private static readonly ConcurrentDictionary<string, DataContract> _contracts = new(StringComparer.Ordinal);

        /// <summary>注册或覆盖一条契约（覆盖 = 版本演进，键不可变）。</summary>
        public static void Register(DataContract contract)
        {
            if (contract is null) throw new ArgumentNullException(nameof(contract));
            _contracts[contract.Key] = contract;
        }

        /// <summary>批量注册（播种器入口）。</summary>
        public static void RegisterRange(IEnumerable<DataContract> contracts)
        {
            if (contracts is null) throw new ArgumentNullException(nameof(contracts));
            foreach (var c in contracts) Register(c);
        }

        /// <summary>按键解析（数据空间写前校验）。</summary>
        public static bool TryGet(string key, out DataContract contract)
            => _contracts.TryGetValue(key, out contract!);

        /// <summary>按键解析，未注册返回 null。</summary>
        public static DataContract? Get(string key)
            => _contracts.TryGetValue(key, out var c) ? c : null;

        /// <summary>是否已注册（写前校验 / 一致性校验器断言）。</summary>
        public static bool IsRegistered(string key) => _contracts.ContainsKey(key);

        /// <summary>全部已注册契约（供一致性校验器枚举审计）。</summary>
        public static ICollection<DataContract> All => _contracts.Values;

        /// <summary>清空（测试隔离用）。</summary>
        public static void Clear() => _contracts.Clear();
    }
}
