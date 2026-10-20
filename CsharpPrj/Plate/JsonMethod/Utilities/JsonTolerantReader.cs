using System;
using System.Text.Json;

namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// 容错 JSON 读取骨架与元素级原语（平台单一实现）：注释 / 尾随逗号容忍；未知键**只收集不抛**
    /// —— 异常类型与域文案留消费方（平台层不绑定任何消费侧身份）。
    /// <para>
    /// 辨异（不得互相替换）：<see cref="Strings"/> 接受裸字符串并包装为单元素数组，
    /// <see cref="StrList"/> 不接受 —— 两组原语服务不同读取入口，语义逐字保留。
    /// </para>
    /// </summary>
    public static class JsonTolerantReader
    {
        /// <summary>解析选项（注释跳过 + 允许尾随逗号）：消费侧自持解析须与之一致。</summary>
        public static readonly JsonDocumentOptions Tolerant = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>解析 → 交给读者 → 释放文档（根元素先 Clone，脱离文档生命周期）。</summary>
        public static T ParseRoot<T>(string json, Func<JsonElement, T> body)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            ArgumentNullException.ThrowIfNull(body);

            using var document = JsonDocument.Parse(json, Tolerant);
            return body(document.RootElement.Clone());
        }

        /// <summary>取对象型段（缺失或非对象 → false）。</summary>
        public static bool TrySection(JsonElement root, string name, out JsonElement section)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                section = value;
                return true;
            }

            section = default;
            return false;
        }

        /// <summary>对象内的未声明键（Ordinal 比较；非对象 → 空）。</summary>
        public static IReadOnlyList<string> UnknownKeys(JsonElement obj, IReadOnlyList<string> known)
        {
            ArgumentNullException.ThrowIfNull(known);
            if (obj.ValueKind != JsonValueKind.Object) return [];

            var unknown = new List<string>();
            foreach (var property in obj.EnumerateObject())
                if (!known.Contains(property.Name, StringComparer.Ordinal))
                    unknown.Add(property.Name);

            return unknown;
        }

        /// <summary>未声明键经消费方文案登记进 <paramref name="violations"/>（不抛；异常身份由消费方决定）。</summary>
        public static void CollectUnknownKeys(
            JsonElement obj, IReadOnlyList<string> known, ICollection<string> violations, Func<string, string> messageFactory)
        {
            ArgumentNullException.ThrowIfNull(violations);
            ArgumentNullException.ThrowIfNull(messageFactory);

            foreach (var name in UnknownKeys(obj, known)) violations.Add(messageFactory(name));
        }

        /// <summary>点链取值（如 <c>out.fields</c>）→ 字符串数组（缺失 → 空）。</summary>
        public static IReadOnlyList<string> PathStrings(JsonElement entry, string dottedPath)
        {
            var current = entry;
            foreach (var segment in dottedPath.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return [];
                current = next;
            }

            return current.ValueKind == JsonValueKind.Array
                ? current.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
                : [];
        }

        /// <summary>字符串数组读取（数组取字符串元素；裸字符串包为单元素；其余 → 空）。</summary>
        public static IReadOnlyList<string> Strings(JsonElement entry, string name)
        {
            if (!entry.TryGetProperty(name, out var value)) return [];

            return value.ValueKind switch
            {
                JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(),
                JsonValueKind.String => [value.GetString()!],
                _ => [],
            };
        }

        /// <summary>字符串读取（缺失或非字符串 → null）。</summary>
        public static string? Text(JsonElement entry, string name)
            => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>数值读取（缺失或非数值 → null）。</summary>
        public static double? Number(JsonElement entry, string name)
            => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
                ? number
                : null;

        /// <summary>字符串数组字段读取（缺失或非数组 → null；**不接受裸字符串**）。</summary>
        public static IReadOnlyList<string>? StrList(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return null;

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray();
        }

        /// <summary>元素数组字段读取（缺失或非数组 → null；元素 Clone 脱离文档生命周期）。</summary>
        public static IReadOnlyList<JsonElement>? Elements(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return null;

            return value.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        /// <summary>整数读取（缺失或非整数 → null）。</summary>
        public static int? Int(JsonElement element, string name)
            => element.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out var number)
                ? number
                : null;

        /// <summary>字符串读取（缺失或非字符串 → null）。</summary>
        public static string? Str(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>布尔读取（缺失或非布尔 → null）。</summary>
        public static bool? Bool(JsonElement element, string name)
            => element.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                }
                : null;

        /// <summary>
        /// 解析根元素；空文本 / 非法 JSON ⇒ null（不抛）。根元素已 Clone，脱离文档生命周期。
        /// <para>与 <see cref="ParseRoot{T}"/> 的差别：把「不可解析」当可判定的空值（容错读），
        /// 适用于「单条数据不可解析不得阻断整批」的场景；契约面 fail-fast 读不得使用本方法。</para>
        /// </summary>
        public static JsonElement? TryRootOrNull(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var text = UnwrapJsonString(json);
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                using var document = JsonDocument.Parse(text, Tolerant);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>字符串若本身是 JSON 对象/数组文本 ⇒ 去引号；否则原样（去首尾空白）。</summary>
        public static string? UnwrapJsonString(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var trimmed = text.Trim();
            if (!(trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)))
            {
                return trimmed;
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed, Tolerant);
                if (document.RootElement.ValueKind != JsonValueKind.String) return trimmed;
                var inner = document.RootElement.GetString();
                if (string.IsNullOrWhiteSpace(inner)) return inner;
                var innerTrimmed = inner.Trim();
                return innerTrimmed.StartsWith("{", StringComparison.Ordinal)
                       || innerTrimmed.StartsWith("[", StringComparison.Ordinal)
                    ? innerTrimmed
                    : trimmed;
            }
            catch (JsonException)
            {
                return trimmed;
            }
        }

        /// <summary>属性原始文本（字符串包裹的 JSON 去引号；非 JSON 字符串返回带引号原文，交解析方显式报错）。</summary>
        public static string? Raw(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? UnwrapJsonString(value.GetString())
                : value.GetRawText();
        }
    }
}
