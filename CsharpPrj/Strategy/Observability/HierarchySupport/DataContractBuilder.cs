using System;
using System.Collections.Generic;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 数据契约流式构建器 —— 统一数据传递标准的「注册即扩展」入口。
    /// 收敛 <see cref="DataContractRegistry.RegisterRange"/> 的散落注册为一条链式声明：
    /// 新增一个参数 = 追加一条 <c>.Add(...)</c>，无需改生产/消费方 5 处联动。
    /// 装配期调用 <see cref="BuildAndRegister"/> 一次性播种（幂等）。
    /// </summary>
    public sealed class DataContractBuilder
    {
        private readonly List<DataContract> _contracts = new();

        public DataContractBuilder Add(
            string key,
            Type valueType,
            DataDirection direction,
            DataScope scope = DataScope.Round,
            int schemaVersion = 1,
            string? producer = null,
            string? consumer = null,
            bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("数据契约键不能为空", nameof(key));
            _contracts.Add(new DataContract(
                key, valueType, direction, schemaVersion, producer, consumer, scope, persist));
            return this;
        }

        /// <summary>追加已构造契约（复用已有契约实例）。</summary>
        public DataContractBuilder Add(DataContract contract)
        {
            _contracts.Add(PortGuard.Required(contract, nameof(contract)));
            return this;
        }

        /// <summary>批量追加（合并其它注册源）。</summary>
        public DataContractBuilder AddRange(IEnumerable<DataContract> contracts)
        {
            contracts = PortGuard.Required(contracts, nameof(contracts));
            _contracts.AddRange(contracts);
            return this;
        }

        public IReadOnlyList<DataContract> Contracts => _contracts;

        /// <summary>把累积的契约批量注册到全局注册表（幂等，覆盖 = 版本演进）。</summary>
        public DataContractBuilder Register()
        {
            DataContractRegistry.RegisterRange(_contracts);
            return this;
        }

        /// <summary>清空已累积的契约声明（测试隔离用）。</summary>
        public DataContractBuilder Clear()
        {
            _contracts.Clear();
            return this;
        }
    }
}
