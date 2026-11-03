using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// JSON 属性读取**策略框架**：大小写不敏感 + 多拼写回退（wire 面 PascalCase / camelCase 混用）。
    /// <para>
    /// 与 <see cref="JsonTolerantReader"/> 的分工：后者是**严格拼写**读取（配置/声明面，拼错即缺值可见）；
    /// 本件是**松弛拼写**读取（外部入参 / 工具载荷面），两者语义不同、不得互相替代。
    /// </para>
    /// <para>纪律：松弛策略只此一处实现，禁各处手写 <c>TryGetProperty</c> + OrdinalIgnoreCase 循环。</para>
    /// </summary>
    public static class JsonFieldAccess
    {
        /// <summary>大小写不敏感取属性（非对象 / 缺失 ⇒ false）。</summary>
        public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(name))
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        /// <summary>按候选拼写顺序取字符串（首个命中且为字符串）；缺失/非字符串 ⇒ null。</summary>
        public static string? Str(JsonElement element, params string[] names)
        {
            if (names is null) return null;
            foreach (var name in names)
            {
                if (name is not null
                    && TryGetProperty(element, name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return null;
        }

        /// <summary>按候选拼写顺序取整数（首个命中且可转 32 位）；否则 null。</summary>
        public static int? Int(JsonElement element, params string[] names)
        {
            if (names is null) return null;
            foreach (var name in names)
            {
                if (name is not null
                    && TryGetProperty(element, name, out var value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out var number))
                {
                    return number;
                }
            }

            return null;
        }

        /// <summary>按候选拼写顺序取布尔；否则 null。</summary>
        public static bool? Bool(JsonElement element, params string[] names)
        {
            if (names is null) return null;
            foreach (var name in names)
            {
                if (name is null || !TryGetProperty(element, name, out var value)) continue;
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
            }

            return null;
        }

        /// <summary>取字符串数组（首个命中且为数组；非字符串元素跳过）。</summary>
        public static IReadOnlyList<string>? Strings(JsonElement element, params string[] names)
        {
            if (names is null) return null;
            foreach (var name in names)
            {
                if (name is null || !TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<string>();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrEmpty(text)) list.Add(text);
                    }
                }

                return list;
            }

            return null;
        }
    }
}
