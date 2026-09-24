#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Modules.Node.Ports;
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
        /// <summary>框架端口准入唯一实现（实例级；诊断经宿主 sink 单向投影到统一日志）。</summary>
        private static readonly NodePortBinding Binding = new(NodeFrameworkLogSink.Instance);

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

            var declared = resultKeys?.Where(key => !string.IsNullOrEmpty(key)).ToArray() ?? Array.Empty<string>();
            var lookup = BuildLookup(result);

            // 成员面未知（简单值 / 无可枚举成员）→ null：落默认端口（由调用方以节点键整存），
            // 与本方法既有语义一致（返回空字典 ⇒ 调用方补默认端口），同时产 port.default 可见化。
            var members = lookup.Count == 0
                ? null
                : lookup.Select(pair => new PortMember(pair.Key, pair.Value is not null)).ToArray();

            var outcome = Binding.ExtractValues(
                nodeKey: string.Empty,
                members,
                valueOf: name => lookup[name],
                defaultValue: result,
                declaredPorts: declared,
                memberComparer: StringComparer.OrdinalIgnoreCase); // 反射/字典键沿用既有宽松匹配

            foreach (var pair in outcome.Ports)
            {
                if (pair.Key.Length > 0) props[pair.Key] = pair.Value;
            }

            return props;
        }

        /// <summary>按 <see cref="NodeConfig"/> 结果键配置提取（ResultKeys 为字符串，做宽松归一化解析；config 可空）。</summary>
        public static Dictionary<string, object?> ExtractResultKeys(NodeConfig? config, object? result)
            => ExtractResultKeys(NormalizeResultKeys(config?.funcPipelineConfig?.ResultKeys), result);

        /// <summary>宽松归一化：剥离 JSON 引号/外部括号后按逗号切分，过滤空白。兼容 "\"r1\"" 与 "a,b"。</summary>
        internal static IEnumerable<string> NormalizeResultKeys(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Enumerable.Empty<string>();
            var text = raw.Trim();

            // JSON 数组形态（真实落库形态，见 KeyProcessor.AddHierarchyToKeysJson 的 JsonSerializer.Serialize）→ 权威解析器。
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                var parsed = KeyProcessor.ParseKeysFromJson(text);
                // 严格 JSON 失败（返回空且非合法空数组 "[]"）→ 兼容旧宽松形态；"[]" → 合法空数组，保持空。
                return (parsed.Count == 0 && text != "[]")
                    ? SplitLegacyCommaForm(text)
                    : parsed;
            }

            // 非 JSON 数组（旧宽松落库/字面量）→ 旧宽松形态。
            return SplitLegacyCommaForm(text);
        }

        /// <summary>旧宽松形态：剥引号/外部括号后按逗号切分，过滤空白（仅供非 JSON 数组输入兼容；JSON 走权威解析器）。</summary>
        private static IEnumerable<string> SplitLegacyCommaForm(string text)
        {
            if (text.StartsWith("[", StringComparison.Ordinal)) text = text.Substring(1).TrimEnd();
            if (text.EndsWith("]", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            return text.Split(',')
                .Select(s => s.Trim().Trim('"'))
                .Where(s => s.Length > 0);
        }

        private static Dictionary<string, object?> BuildLookup(object result)
        {
            var lookup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (result is IDictionary<string, object?> dict)
            {
                foreach (var kv in dict) lookup[kv.Key] = kv.Value;
                return lookup;
            }
            if (IsSimpleValue(result))
                return lookup; // 简单值 → 不反射子成员（String/Length 等无意义成员），整存交由调用方默认端口
            foreach (var p in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0)
                    lookup[p.Name] = p.GetValue(result);
            return lookup;
        }

        private static bool IsSimpleValue(object value)
        {
            return value is string or Enum or char or bool
                or byte or sbyte or short or ushort or int or uint
                or long or ulong or float or double or decimal
                or DateTime or DateTimeOffset or TimeSpan or Guid
                or DateOnly or TimeOnly;
        }
    }
}