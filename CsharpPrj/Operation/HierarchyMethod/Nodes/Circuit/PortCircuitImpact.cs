using System;
using System.Collections.Generic;
using System.Linq;
using DataCenter.Protocol.Standards;
using Share.CircuitFramework.Analysis;
using Share.CircuitFramework.Symbols;
using Ckt = Share.CircuitFramework.Core.Circuit;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 端口引用影响面（CF-2 只读命令）：回答「谁引用了端口 X」（直接 / 传递）。
    /// <para>
    /// 引用关系载体 = 端口声明的 <see cref="PortStandard.Depends"/>（仅取「目标是已声明端口键」者 ⇒ 端口→端口引用；
    /// 非端口键的目标是代码符号，不属端口引用域）。关系不另立台账：**边集即声明的镜像**（对账恒等）。
    /// </para>
    /// <para>
    /// 复用框架能力：每端口建一张电路（<c>Id = 端口键</c>），依赖边落 <see cref="CallSymbol"/>，
    /// 经 <see cref="CircuitCallGraph.GetImpactedBy"/> 做反向可达（框架零改动、框架内零领域类型）。
    /// </para>
    /// </summary>
    public static class PortCircuitImpact
    {
        /// <summary>一条端口→端口引用（<paramref name="From"/> 依赖 <paramref name="To"/>）。</summary>
        public sealed record PortReference(string From, string To);

        /// <summary>端口引用图（不可变只读视图）。</summary>
        public sealed class PortReferenceGraph
        {
            private readonly IReadOnlyList<PortReference> _edges;
            private readonly IReadOnlyDictionary<string, Ckt> _registry;

            internal PortReferenceGraph(
                IReadOnlyList<string> portKeys,
                IReadOnlyList<PortReference> edges,
                IReadOnlyDictionary<string, Ckt> registry)
            {
                PortKeys = portKeys;
                _edges = edges;
                _registry = registry;
            }

            /// <summary>全部已声明端口键（升序）。</summary>
            public IReadOnlyList<string> PortKeys { get; }

            /// <summary>全部端口→端口引用边（声明序去重）。</summary>
            public IReadOnlyList<PortReference> Edges => _edges;

            /// <summary>直接引用了 <paramref name="targetKey"/> 的端口（升序）。</summary>
            public IReadOnlyList<string> DirectReferencingPorts(string targetKey)
                => _edges.Where(e => string.Equals(e.To, targetKey, StringComparison.Ordinal))
                         .Select(e => e.From)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(k => k, StringComparer.Ordinal)
                         .ToList();

            /// <summary>（传递）引用了 <paramref name="targetKey"/> 的端口，不含自身（升序）；目标非端口键 ⇒ 空集。</summary>
            public IReadOnlyList<string> GetReferencingPorts(string targetKey)
            {
                if (string.IsNullOrEmpty(targetKey) || !_registry.TryGetValue(targetKey, out var circuit))
                    return Array.Empty<string>();

                return new CircuitCallGraph(circuit)
                    .GetImpactedBy(targetKey, _registry)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>
        /// 由端口声明集合构建引用图。重复端口键取首次声明（同键多声明属装配面问题，非本命令承担）。
        /// </summary>
        public static PortReferenceGraph Build(IEnumerable<PortStandard> ports)
        {
            ArgumentNullException.ThrowIfNull(ports);

            var distinct = new List<PortStandard>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in ports)
                if (port is not null && !string.IsNullOrEmpty(port.Key) && seenKeys.Add(port.Key))
                    distinct.Add(port);

            var keys = new HashSet<string>(distinct.Select(p => p.Key), StringComparer.Ordinal);

            var edges = new List<PortReference>();
            var seenEdges = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in distinct)
                foreach (var dep in port.Depends ?? Array.Empty<string>())
                    if (keys.Contains(dep) && seenEdges.Add(port.Key + "\u001F" + dep))
                        edges.Add(new PortReference(port.Key, dep));

            var registry = new Dictionary<string, Ckt>(StringComparer.Ordinal);
            foreach (var port in distinct)
                registry[port.Key] = new Ckt(port.Key);
            foreach (var edge in edges)
                registry[edge.From].Append(new CallSymbol { CallerNodeId = edge.From, Target = new CalleeRef(edge.To) });

            return new PortReferenceGraph(
                distinct.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList(),
                edges,
                registry);
        }
    }
}
