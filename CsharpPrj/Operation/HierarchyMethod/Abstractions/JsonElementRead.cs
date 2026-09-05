using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Operation.HierarchyMethod.Abstractions
{
    /// <summary>
    /// <see cref="JsonElement"/> / <see cref="Utf8JsonWriter"/> 安全读取与写入工具（通用框架，下沉自 Agent.Core）。
    /// 读取均带 <see cref="JsonValueKind"/> 类型守卫，缺失/类型不符时回退默认值（不抛），供各实现共用单一逻辑避免双源漂移。
    /// </summary>
    public static class JsonElementRead
    {
        /// <summary>安全读字符串属性：obj 非对象 / 属性缺失 / 非字符串 → null。</summary>
        public static string? GetStr(JsonElement obj, string name)
            => obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(name, out var e)
                && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;

        /// <summary>安全读整型属性：obj 非对象 / 属性缺失 / 非数值 / 溢出 → fallback。</summary>
        public static int GetInt(JsonElement obj, string name, int fallback)
            => obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(name, out var e)
                && e.ValueKind == JsonValueKind.Number
                && e.TryGetInt32(out var v)
                ? v
                : fallback;

        /// <summary>安全读布尔属性：obj 非对象 / 属性缺失 / 非布尔 → fallback。</summary>
        public static bool GetBool(JsonElement obj, string name, bool fallback)
            => obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(name, out var e)
                && e.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? e.GetBoolean()
                : fallback;

        /// <summary>安全解析枚举（忽略大小写）；无法解析 → fallback。</summary>
        public static TEnum TryEnum<TEnum>(string? value, TEnum fallback)
            where TEnum : struct
            => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

        /// <summary>可空字符串属性写入：null 跳过（保持 JSON 简洁），非 null 写字符串。</summary>
        public static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
        {
            if (value is null) return;
            writer.WriteString(name, value);
        }
    }
}
