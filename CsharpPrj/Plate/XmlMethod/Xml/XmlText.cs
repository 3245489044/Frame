using System.Collections;
using System.Globalization;
using System.Text;

namespace Plate.XmlMethod.Xml
{
    /// <summary>
    /// XML 文本转义工具：把用户/数据内容安全地转成 XML 文本或属性值。
    /// 在处理 HTML/SVG 时需要"文本内容不可当标签解析"，均应经 XmlText 产出，避免注入。
    /// </summary>
    public static class XmlText
    {
        /// <summary>转义后作为元素文本/属性值使用（保留引号实体，属性值也安全）。</summary>
        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 转换为 XML 属性文本：null → 空串；bool → 小写；数值/枚举/时刻取不变文本；
        /// **字典 → <c>{k1=v1; k2=v2}</c>（键按序数排序）、集合 → <c>[v1; v2]</c>**（逐层递归投影）。
        /// <para>
        /// 纪律：字典/集合**禁止**经 <c>ToString()</c> 落成类型名（如 <c>System.Collections.Generic.Dictionary`2[...]</c>）。
        /// 属性是文本槽，这里给的是**确定性文本投影**（非 JSON 语法），消费方直接当文本使用，不得再解析回结构。
        /// </para>
        /// </summary>
        public static string ToAttrValue(object? value) => FormatValue(value);

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
