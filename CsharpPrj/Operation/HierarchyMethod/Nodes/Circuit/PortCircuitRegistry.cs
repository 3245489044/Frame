using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DataCenter.Protocol.Standards;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Projection;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 端口电路编译产物缓存（CF-2）：按**内容指纹**缓存 <see cref="PortCircuitProjector"/> 的投影 + 编译结果，
    /// 消「每次装配重建」之费（对齐既有 <c>RoundFlowCircuitRegistry</c> 范式）。
    /// <para>
    /// 缓存键 = 端口键 + 声明内容指纹（<see cref="PortStandard"/> 全量序列化）：**同名不同内容必重建**
    /// （声明面热改 / 多宿主同名端口），不复用陈旧电路；内容未变则命中缓存，零重复编译。
    /// </para>
    /// <para>
    /// 编译失败**仍入缓存**（承载诊断，供诊断/守卫消费；与「失败不写缓存」的执行面注册表语义不同 ——
    /// 本件是**只读诊断面**，不接执行）。构建体无共享可变状态，故无需全局锁。
    /// </para>
    /// </summary>
    public static class PortCircuitRegistry
    {
        /// <summary>单端口编译产物（配方 + 诊断；<see cref="Compiled"/> = 是否编译通过）。</summary>
        public sealed record BuiltCircuit(
            string PortKey,
            CircuitRecipeSpec Recipe,
            bool Compiled,
            IReadOnlyList<CircuitDiagnostic> Diagnostics);

        private static readonly ConcurrentDictionary<string, BuiltCircuit> Cache = new(StringComparer.Ordinal);

        /// <summary>键分隔符（指纹前缀 = 端口键，供 <see cref="Invalidate"/> 按端口名失效）。</summary>
        private const char KeySeparator = '\u001F';

        /// <summary>已缓存条数（诊断 / 门禁正对照用）。</summary>
        public static int Count => Cache.Count;

        /// <summary>取（或首次构建）该端口的电路编译产物；同一内容指纹只构建一次。</summary>
        public static BuiltCircuit GetOrBuild(PortStandard port)
        {
            ArgumentNullException.ThrowIfNull(port);
            return Cache.GetOrAdd(Fingerprint(port), _ => Build(port));
        }

        /// <summary>按端口键失效（声明面热改后调用；无匹配 = no-op）。</summary>
        public static void Invalidate(string portKey)
        {
            var prefix = (portKey ?? string.Empty) + KeySeparator;
            foreach (var key in Cache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Cache.TryRemove(key, out _);
        }

        /// <summary>清空缓存（门禁 / 测试用）。</summary>
        public static void Clear() => Cache.Clear();

        /// <summary>
        /// 内容指纹（**投影输入全集**）：端口键 + <see cref="PortStandard"/> 全量 JSON 序列化。
        /// 声明任一字段变 ⇒ 指纹变 ⇒ 重建（不依赖 <c>Name</c> 标识，防同名复用陈旧电路）。
        /// </summary>
        internal static string Fingerprint(PortStandard port)
            => (port.Key ?? string.Empty) + KeySeparator + JsonSerializer.Serialize(port);

        private static BuiltCircuit Build(PortStandard port)
        {
            var result = PortCircuitProjector.Project(port);
            return new BuiltCircuit(result.PortKey, result.Recipe, result.Compiled, result.Diagnostics);
        }
    }
}
