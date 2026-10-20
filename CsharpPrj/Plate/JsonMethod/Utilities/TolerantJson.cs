using System;
using System.Text.Json;

namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// 容错反序列化（平台单一实现）：空文本 / 非法 JSON ⇒ null（不抛）。
    /// <para>辨异：本类服务「单条消息 / 单个帧不可解析不得阻断整轮」的宽容读；
    /// 对外契约的 fail-fast 解析仍走序列化服务，不得互相替换。</para>
    /// </summary>
    public static class TolerantJson
    {
        /// <summary>反序列化为 <typeparamref name="T"/>；空文本 / 非法 JSON → null（不抛）。</summary>
        /// <param name="json">JSON 全文。</param>
        /// <param name="options">序列化选项（缺省 = 默认选项）。</param>
        public static T? TryDeserialize<T>(string? json, JsonSerializerOptions? options = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json, options); }
            catch (JsonException) { return null; }
        }

        /// <summary>解析为 **JSON 对象**元素；空文本 / 非法 JSON / 非对象形态 → null（不抛）。</summary>
        /// <param name="json">JSON 全文。</param>
        /// <param name="options">序列化选项（缺省 = 默认选项）。</param>
        public static JsonElement? TryObject(string? json, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(json, options);
                return element.ValueKind == JsonValueKind.Object ? element : null;
            }
            catch (JsonException) { return null; }
        }
    }
}
