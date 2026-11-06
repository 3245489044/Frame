#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Model;
using Modules.KeySpace.Runtime;
using Modules.Node.Materialization;
using WorkFlowMethod.KeySpace.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Diagnostics;
using WorkFlowMethod.Skeletons.NodeProcess.KeyMapping;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点结果 → 输出端口提取器（泛化 Agent NodeExecutionService.ExtractProps）。
    /// 端口准入规则唯一实现 = <see cref="NodePortBinding.ExtractValues{TValue}"/>（规则 = NodePortRules）：
    ///   ① 明确 <paramref name="resultKeys"/> 时，仅取这些端口（多节点同名结果键 → 定向全写不反射错配）；
    ///   ② 未给 resultKeys 时，按结果类型成员推断，且必产 <c>port.inferred</c> 诊断（治 P12：禁静默反射猜端口）；
    ///   ③ null 结果 → 空端口（对齐 InitialStateNullity）；null 端口值不产槽位；
    ///   ④ 简单值/无可枚举成员 → 默认端口整存由调用方以节点键落（<c>port.default</c> 可见化）。
    /// 结果值提取优先：result 是 <see cref="IDictionary{TKey,TValue}"/> → 按键直查；否则反射公共属性；
    /// 值保持宿主原生对象（不经 JSON 往返，保对象身份）。
    /// 特化钩子（E2）：静态注册表 <see cref="RegisterSpecialized"/>，按「结果类型可赋值」命中时使用注册的
    /// 特化提取器（整对象定向全写），否则回落到上述默认规则。消除了框架与 Agent 侧针对强类型执行结果
    /// （AgentExecutorResult）的反射分叉：特化实际类型以命中项为准，不依赖属性名反射。
    /// </summary>
    public static class NodeResultExtractor
    {
        private sealed class SpecializedExtractor
        {
            public required Type ResultType;
            public required Func<object, IEnumerable<string>, Dictionary<string, object?>> Extract;
        }

        // E2 特化提取器注册表：注册顺序即匹配优先级；命中「ResultType.IsInstanceOfType(result)」的首项。
        private static readonly ConcurrentQueue<SpecializedExtractor> _specialized = new();

        /// <summary>
        /// 注册按结果类型触发的特化提取器。触发条件：<paramref name="resultType"/> 可赋值给提取时的实参结果
        /// （即 resultType.IsInstanceOfType(result)）。可注册基类型，其派生实际结果亦会命中。
        /// </summary>
        public static void RegisterSpecialized(
            Type resultType,
            Func<object, IEnumerable<string>, Dictionary<string, object?>> extractor)
        {
            ArgumentNullException.ThrowIfNull(resultType);
            ArgumentNullException.ThrowIfNull(extractor);
            _specialized.Enqueue(new SpecializedExtractor { ResultType = resultType, Extract = extractor });
        }

        /// <summary>按 type 匹配特化提取器；无命中返回 null（registered.IsAssignableFrom(resultType)）。线程安全。</summary>
        private static Func<object, IEnumerable<string>, Dictionary<string, object?>>? ResolveSpecialized(Type resultType)
        {
            foreach (var entry in _specialized)
                if (entry.ResultType.IsAssignableFrom(resultType))
                    return entry.Extract;
            return null;
        }

        /// <summary>
        /// 查询某结果类型是否已注册特化提取器（供消费方按「是否已特化」决定分支，
        /// 替代对具体强类型结果（如 Agent 侧执行结果）的硬编码类型判定）。线程安全。
        /// </summary>
        public static bool HasSpecialized(Type resultType)
            => resultType is not null && ResolveSpecialized(resultType) is not null;

        /// <summary>按显式结果键集合定向提取端口（框架纯核心）。</summary>
        public static Dictionary<string, object?> ExtractResultKeys(IEnumerable<string>? resultKeys, object? result)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (result is null) return props;                       // null 结果 → 无产出端口

            var specialized = ResolveSpecialized(result.GetType());
            if (specialized is not null)
            {
                // E2：特化提取（如 AgentExecutorResult → 按 resultKeys 整对象定向全写），不反射属性名。
                return specialized(result, resultKeys ?? Enumerable.Empty<string>());
            }

            // 默认提取单源 = Modules.Node.Materialization.NodeResultReader（L1 通用件）：
            // 准入规则（NodePortRules）、显式 null 留痕、简单值默认端口、诊断投影全部由其承载。
            return NodeResultReader.Read(resultKeys, result, NodeFrameworkLogSink.Instance);
        }

        /// <summary>
        /// 契约驱动的输出提取（通用实现 = <see cref="KeyBinder.BindOut"/>）：Required 缺失可见，
        /// Optional 缺失不产槽位；契约未覆盖时由调用方回落旧推断路径。
        /// </summary>
        public static Dictionary<string, object?> ExtractResultKeys(
            IEnumerable<string>? resultKeys,
            object? result,
            IReadOnlyList<PortContract> contracts)
        {
            if (contracts.Count == 0)
            {
                return ExtractResultKeys(resultKeys, result);
            }

            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (result is null)
            {
                return props;
            }

            var lookup = NodeResultReader.BuildLookup(result);
            var binding = new KeyBinder().BindOut(contracts, lookup);
            if (binding.RequiredMissing)
            {
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Warn(
                    "NodePort", "port.required-missing: 契约声明的 Required 输出端口未产出", null);
            }

            foreach (var pair in binding.Values)
            {
                if (pair.Value.State == KeyValueState.Known)
                {
                    // Known + null 亦是事实（显式空），不得静默丢弃。
                    props[pair.Key] = pair.Value.Value;
                }
            }

            return props;
        }

        /// <summary>按 <see cref="NodeConfig"/> 结果键配置提取（ResultKeys 为字符串，做宽松归一化解析；config 可空）。</summary>
        public static Dictionary<string, object?> ExtractResultKeys(NodeConfig? config, object? result)
        {
            var keys = NormalizeResultKeys(config?.funcPipelineConfig?.ResultKeys);
            var contracts = NodePortContractRegistry.ForNode(config);
            return contracts.Count > 0
                ? ExtractResultKeys(keys, result, contracts)
                : ExtractResultKeys(keys, result);
        }

        /// <summary>
        /// 宽松归一化（A2）：JSON 数组 / 历史逗号形态均经 <see cref="KeySetParse"/> 单入口；
        /// 解析失败产出诊断而非静默空集；空集必带 <see cref="EmptySetPolicy.Report"/> 理由。
        /// </summary>
        internal static IEnumerable<string> NormalizeResultKeys(string? raw)
        {
            var parsed = KeySetParse.Parse(raw, "workflow.result-keys");
            if (parsed.Diagnostic is not null)
            {
                WorkflowKeyDiagnosticsSink.Instance.Report(parsed.Diagnostic);
            }

            return parsed.Members;
        }
    }
}