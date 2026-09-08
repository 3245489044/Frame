#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点结果 → 输出端口提取器（泛化 Agent NodeExecutionService.ExtractProps）。
    /// 规则：
    ///   ① 明确 <paramref name="resultKeys"/> 时，仅取这些端口（多节点同名结果键 → 定向全写不反射错配）；
    ///   ② 未给 resultKeys 时，按结果类型公共实例属性名反射兜底；
    ///   ③ null 结果 → 空端口（对齐 InitialStateNullity）；null 端口值跳过、不产生空槽位。
    /// 结果值提取优先：result 是 <see cref="IDictionary{TKey,TValue}"/> → 按键直查；否则反射公共属性。
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

            var lookup = BuildLookup(result);
            if (resultKeys is null || !resultKeys.Any())
            {
                // 反射兜底：未指明结果键（或键集为空）→ 全部公共属性，null 值跳过
                foreach (var kv in lookup)
                    if (kv.Value != null)
                        props[kv.Key] = kv.Value;
                return props;
            }

            foreach (var port in resultKeys)
            {
                if (string.IsNullOrEmpty(port)) continue;
                if (lookup.TryGetValue(port, out var v) && v != null)
                    props[port] = v;                                // 未命中/为 null → 静默跳过
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
            if (text.StartsWith("[")) text = text.Substring(1, text.Length - 1).TrimEnd();
            if (text.EndsWith("]")) text = text.Substring(0, text.Length - 1);
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