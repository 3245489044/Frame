using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Modules.Parsing.Parsing;

namespace Modules.Parsing.Rules
{
    /// <summary>
    /// 通用 JSON 条件判定引擎（领域无关，零 LLM）。
    /// 对 JsonElement 数据按 field/op/value 三元组求值；同一内核可服务规则匹配、重规划判定等多种消费方
    /// （Agent.Core 的 RuleEngine 即委托本内核，对齐单源实现原则）。
    /// 运算符：true/false/==/!=/&gt;/&gt;=/&lt;/&lt;=/contains/contains_any/not_contains；
    /// 数组比较支持集合相等与成员关系；数值比较经 decimal 判定；字段经 <see cref="JsonPathHelper.NavigateTo"/> 解析点路径。
    /// 未知 op 记日志（可注入回调）并返回 false。
    /// </summary>
    public static class JsonRuleEngine
    {
        /// <summary>求值单条条件（字符串 "true"/"false" 或对象 {field,op,value}）。</summary>
        public static bool EvaluateCondition(JsonElement cond, JsonElement data)
            => EvaluateCondition(cond, data, null);

        /// <summary>求值单条条件，未知 op 时回调 onUnknownOperator(op, field)。</summary>
        public static bool EvaluateCondition(JsonElement cond, JsonElement data, Action<string, string?>? onUnknownOperator)
        {
            if (cond.ValueKind == JsonValueKind.String)
                return string.Equals(cond.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            if (cond.ValueKind != JsonValueKind.Object) return false;

            var field = cond.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            var op = cond.TryGetProperty("op", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : "true";
            var value = cond.TryGetProperty("value", out var v) ? v : default;
            return Evaluate(data, field, op, value, onUnknownOperator);
        }

        /// <summary>对数据按 field/op/value 求值。</summary>
        public static bool Evaluate(JsonElement data, string? field, string op, JsonElement value)
            => Evaluate(data, field, op, value, null);

        /// <summary>对数据按 field/op/value 求值，未知 op 时回调 onUnknownOperator(op, field)。</summary>
        public static bool Evaluate(JsonElement data, string? field, string op, JsonElement value, Action<string, string?>? onUnknownOperator)
        {
            switch (op?.ToLowerInvariant())
            {
                case "true": return true;
                case "false": return false;
                case "==": return AreEqual(GetField(data, field), value);
                case "!=": return !AreEqual(GetField(data, field), value);
                case ">": return CompareNum(GetField(data, field), value) is int gt && gt > 0;
                case ">=": return CompareNum(GetField(data, field), value) is int gte && gte >= 0;
                case "<": return CompareNum(GetField(data, field), value) is int lt && lt < 0;
                case "<=": return CompareNum(GetField(data, field), value) is int lte && lte <= 0;
                case "contains": return Contains(data, field, value);
                case "contains_any": return ContainsAny(data, field, value);
                case "not_contains": return !Contains(data, field, value);
                default:
                    // 未知 op（拼写错误）不再静默返回 false，记录日志以暴露配置错误。
                    onUnknownOperator?.Invoke(op, field);
                    return false;
            }
        }

        private static object? GetField(JsonElement data, string? field)
        {
            if (string.IsNullOrEmpty(field)) return null;
            var el = JsonPathHelper.NavigateTo(data, field);
            if (el is null || el.Value.ValueKind == JsonValueKind.Null || el.Value.ValueKind == JsonValueKind.Undefined)
                return null;
            return el.Value.ValueKind switch
            {
                JsonValueKind.String => el.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => el.Value.TryGetInt32(out var i) ? (object?)i : el.Value.GetDouble(),
                // 数组保形返回 object?[]（避免拼接为逗号字符串导致数组-数组比较恒 false）。
                JsonValueKind.Array => el.Value.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? (object?)e.GetString() : e.ToString())
                    .ToArray(),
                _ => el.Value.ToString()
            };
        }

        /// <summary>把 GetField 的取值归一化为可比文本（数组 → 逗号拼接，保持原 contains 子串语义）。</summary>
        private static string ToText(object? value)
        {
            if (value is string s) return s;
            if (value is IEnumerable<object?> arr) return string.Join(",", arr);
            return value?.ToString() ?? "";
        }

        private static bool AreEqual(object? actual, JsonElement expected)
        {
            if (expected.ValueKind == JsonValueKind.Undefined) return actual == null;
            if (actual is null) return false;
            return expected.ValueKind switch
            {
                JsonValueKind.True => actual is bool tb && tb,
                JsonValueKind.False => actual is bool fb && !fb,
                JsonValueKind.String => string.Equals(actual.ToString(), expected.GetString(), StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => CompareNum(actual, expected) == 0,
                JsonValueKind.Array => ArrayMatches(actual, expected),
                _ => string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase)
            };
        }

        /// <summary>数组比较：actual 为数组 → 与 expected 集合相等（元素级、顺序无关）；actual 为标量 → 成员关系（actual ∈ expected）。</summary>
        private static bool ArrayMatches(object? actual, JsonElement expected)
        {
            if (expected.ValueKind != JsonValueKind.Array) return false;
            var expectedItems = expected.EnumerateArray().Select(e => e.ToString()).ToArray();

            if (actual is IEnumerable<object?> actualArr)
            {
                var actualItems = actualArr.Select(x => x?.ToString()).ToArray();
                if (expectedItems.Length != actualItems.Length) return false;
                // 集合相等：双向包含（忽略大小写，顺序无关）。
                return expectedItems.All(x => actualItems.Contains(x, StringComparer.OrdinalIgnoreCase))
                    && actualItems.All(x => expectedItems.Contains(x, StringComparer.OrdinalIgnoreCase));
            }

            // 标量 → 成员关系（actual ∈ expected），保持原语义。
            return expected.EnumerateArray().Any(e => AreEqual(actual, e));
        }

        private static int? CompareNum(object? actual, JsonElement expected)
        {
            if (expected.ValueKind != JsonValueKind.Number || !decimal.TryParse(actual?.ToString(), out var act))
                return null;
            return expected.TryGetDecimal(out var exp) ? act.CompareTo(exp) : null;
        }

        private static bool Contains(JsonElement data, string? field, JsonElement expected)
        {
            if (expected.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(field)) return false;
            var actual = ToText(GetField(data, field));
            return actual.IndexOf(expected.GetString()!, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAny(JsonElement data, string? field, JsonElement expected)
        {
            if (expected.ValueKind != JsonValueKind.Array || string.IsNullOrEmpty(field)) return false;
            var actual = ToText(GetField(data, field));
            foreach (var item in expected.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    actual.IndexOf(item.GetString()!, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
