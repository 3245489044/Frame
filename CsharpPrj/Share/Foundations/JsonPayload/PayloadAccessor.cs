using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Share.JsonPayload
{
    /// <summary>
    /// JSON 载荷取值**单一实现**：对 <see cref="JsonElement"/> / <see cref="JsonElement"/>? + 属性名
    /// 的「ValueKind==Object ∧ TryGetProperty ∧ 类型守卫」逐字同构逻辑统一到此处。
    /// 纯 BCL，零领域语义；API / 总线 / UI 入口共用，禁止各处手写裸 GetString / TryGetProperty。
    /// </summary>
    public static class PayloadAccessor
    {
        /// <summary>可空载荷取字符串；非对象/缺键/非字符串 → null。</summary>
        public static string? Str(JsonElement? payload, string prop)
            => payload is { ValueKind: JsonValueKind.Object } p ? Str(p, prop) : null;

        /// <summary>对象载荷取字符串；缺键/非字符串 → null。</summary>
        public static string? Str(JsonElement root, string prop)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        /// <summary>取子元素（Clone 脱离父文档生命周期）；缺键 → null。</summary>
        public static JsonElement? Element(JsonElement? payload, string prop)
            => payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(prop, out var v) ? v.Clone() : null;

        /// <summary>对象载荷取属性原文（任意 ValueKind 以 <c>ToString()</c> 表达，含数字/布尔/子对象）；缺键 → null。</summary>
        public static string? Text(JsonElement root, string prop)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var v) ? v.ToString() : null;

        /// <summary>取 32 位整数；缺键/非数字 → null。</summary>
        public static int? Int(JsonElement? payload, string prop)
            => payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(prop, out var v)
               && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : (int?)null;

        /// <summary>取 32 位整数（带缺省）；缺键/非数字 → <paramref name="dflt"/>。</summary>
        public static int Int(JsonElement? payload, string prop, int dflt) => Int(payload, prop) ?? dflt;

        /// <summary>取布尔；缺键/非布尔 → null（三态）。</summary>
        public static bool? Bool(JsonElement? payload, string prop)
            => payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(prop, out var v)
               && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : (bool?)null;

        /// <summary>取字符串数组；缺键/非数组 → null（数组内非字符串元素被过滤）。</summary>
        public static IReadOnlyList<string>? Strings(JsonElement? payload, string prop)
            => payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : null;
    }
}
