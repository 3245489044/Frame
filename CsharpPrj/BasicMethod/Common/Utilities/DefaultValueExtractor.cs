
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Modules.Common.Utilities
{
    /// <summary>
    /// 默认值提取器：把参数载体按名取出为**文本**。
    /// <para>
    /// 纪律：文本按值形态确定产出 —— 标量取不变文本，**字典 → <c>{k1=v1; k2=v2}</c>、集合 → <c>[v1; v2]</c>**，
    /// 禁止把字典/集合经 <c>ToString()</c> 降级成类型名（<c>System.Collections.Generic.Dictionary`2[...]</c>）。
    /// 契约保持 <c>string?</c>：调用方把结果当参数文本消费，不再解析回结构。
    /// </para>
    /// </summary>
    public class DefaultValueExtractor : IValueExtractor
    {
        public string? ExtractValue(object? parameters, string key)
        {
            if (parameters == null || string.IsNullOrEmpty(key)) return null;

            if (parameters is Dictionary<string, object?> dict)
                return dict.TryGetValue(key, out var val) ? FormatValue(val) : null;

            if (parameters is Dictionary<string, string> strDict)
                return strDict.TryGetValue(key, out var val) ? val : null;

            if (parameters is IReadOnlyDictionary<string, object?> readOnlyDict)
                return readOnlyDict.TryGetValue(key, out var val) ? FormatValue(val) : null;

            var type = parameters.GetType();
            var prop = type.GetProperty(key);
            if (prop != null && prop.CanRead)
            {
                var value = prop.GetValue(parameters);
                return FormatValue(value);
            }

            var field = type.GetField(key);
            if (field != null)
            {
                var value = field.GetValue(parameters);
                return FormatValue(value);
            }

            return null;
        }

        public IReadOnlyDictionary<string, string?> ExtractAllValues(object? parameters)
        {
            var result = new Dictionary<string, string?>();

            if (parameters == null) return result;

            if (parameters is Dictionary<string, object?> dict)
            {
                foreach (var kvp in dict) result[kvp.Key] = FormatValue(kvp.Value);
                return result;
            }

            if (parameters is Dictionary<string, string> strDict)
            {
                foreach (var kvp in strDict) result[kvp.Key] = kvp.Value;
                return result;
            }

            if (parameters is IReadOnlyDictionary<string, object?> readOnlyDict)
            {
                foreach (var kvp in readOnlyDict) result[kvp.Key] = FormatValue(kvp.Value);
                return result;
            }

            var type = parameters.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (prop.CanRead)
                {
                    var value = prop.GetValue(parameters);
                    result[prop.Name] = FormatValue(value);
                }
            }

            foreach (var field in type.GetFields())
            {
                if (!result.ContainsKey(field.Name))
                {
                    var value = field.GetValue(parameters);
                    result[field.Name] = FormatValue(value);
                }
            }

            return result;
        }

        /// <summary>值 → 文本：标量取不变文本；字典/集合按结构递归投影；其余对象才退回 <c>ToString()</c>。</summary>
        private static string FormatValue(object? value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case bool flag: return flag ? "true" : "false";
                case string text: return text;
                case Enum enumValue: return enumValue.ToString();
                case DateTimeOffset stamp: return stamp.ToString("O");
                case DateTime stamp: return stamp.ToString("O");
                case sbyte number: return number.ToString(CultureInfo.InvariantCulture);
                case byte number: return number.ToString(CultureInfo.InvariantCulture);
                case short number: return number.ToString(CultureInfo.InvariantCulture);
                case ushort number: return number.ToString(CultureInfo.InvariantCulture);
                case int number: return number.ToString(CultureInfo.InvariantCulture);
                case uint number: return number.ToString(CultureInfo.InvariantCulture);
                case long number: return number.ToString(CultureInfo.InvariantCulture);
                case ulong number: return number.ToString(CultureInfo.InvariantCulture);
                case float number: return number.ToString(CultureInfo.InvariantCulture);
                case double number: return number.ToString(CultureInfo.InvariantCulture);
                case decimal number: return number.ToString(CultureInfo.InvariantCulture);
            }

            if (TryDictionaryEntries(value, out var entries))
            {
                var parts = new List<string>(entries.Count);
                foreach (var entry in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    parts.Add(entry.Key + "=" + FormatValue(entry.Value));
                }

                return "{" + string.Join("; ", parts) + "}";
            }

            if (value is IEnumerable sequence)
            {
                var parts = new List<string>();
                foreach (var item in sequence)
                {
                    parts.Add(FormatValue(item));
                }

                return "[" + string.Join("; ", parts) + "]";
            }

            return value.ToString() ?? string.Empty;
        }

        /// <summary>字符串键字典探测（非泛型 <see cref="IDictionary"/> + 泛型只读/可变字典接口兜底）。</summary>
        private static bool TryDictionaryEntries(
            object value, out IReadOnlyList<KeyValuePair<string, object?>> entries)
        {
            if (value is IDictionary dictionary)
            {
                var mapped = new List<KeyValuePair<string, object?>>(dictionary.Count);
                foreach (DictionaryEntry entry in dictionary)
                {
                    mapped.Add(new KeyValuePair<string, object?>(
                        entry.Key?.ToString() ?? string.Empty, entry.Value));
                }

                entries = mapped;
                return true;
            }

            var contract = value.GetType().GetInterfaces().FirstOrDefault(type =>
                type.IsGenericType
                && type.GetGenericArguments().Length == 2
                && type.GetGenericArguments()[0] == typeof(string)
                && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                    || type.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
            if (contract is null)
            {
                entries = Array.Empty<KeyValuePair<string, object?>>();
                return false;
            }

            var keys = contract.GetProperty("Keys")!.GetValue(value) as IEnumerable;
            var indexer = contract.GetProperty("Item")!;
            var items = new List<KeyValuePair<string, object?>>();
            foreach (var key in keys!)
            {
                items.Add(new KeyValuePair<string, object?>(
                    key.ToString() ?? string.Empty, indexer.GetValue(value, new[] { key })));
            }

            entries = items;
            return true;
        }
    }
}
