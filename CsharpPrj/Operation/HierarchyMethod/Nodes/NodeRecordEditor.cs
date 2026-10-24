using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点记录编辑器（写侧聚拢点，<see cref="NodeOutputCodec"/> 的对偶件）——把「任意 nodeType 记录」的中立编辑形态
    /// （单 JSON 承载五字段）与既有单键写端口之间的「编解码 + 白名单 + 身份护栏」收敛为一处，供单条保存 / 批量修改复用。
    /// <para>
    /// 框架 LLM-free、零领域依赖：仅依赖 DataStoreDriven（<see cref="NodeRecord"/>/<see cref="NodeTypes"/>）+ 平台 JSON 框架。
    /// 纪律：NodeKey 为主键不可改（身份由展开键承载）；nodeType 越 <see cref="NodeTypes.LogicRecordTypes"/> 白名单 ⇒ 拒绝可见化。
    /// </para>
    /// </summary>
    public static class NodeRecordEditor
    {
        /// <summary>节点记录 → 编辑器 JSON（五字段单框；payload 以原始 JSON 直出，避免二次转义）。</summary>
        public static string ToEditorJson(NodeRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("nodeType", record.NodeType);
                writer.WriteString("threadId", record.ThreadId);
                writer.WriteString("key", record.Key);
                writer.WriteString("status", record.Status);
                writer.WritePropertyName("payload");
                var payloadJson = UsharpValueJson.ToJson(record.Payload);
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteRawValue(payloadJson);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>按五字段构造编辑器 JSON（载荷 = 已解码的记录体 JSON；与 <see cref="ToEditorJson(NodeRecord)"/> 同形）。</summary>
        /// <param name="nodeType">节点类型。</param>
        /// <param name="threadId">线程/工作流 Id。</param>
        /// <param name="key">节点键。</param>
        /// <param name="status">状态。</param>
        /// <param name="payloadJson">记录体 JSON（空 ⇒ null 载荷；非法 JSON ⇒ 转义保事实）。</param>
        public static string ToEditorJson(string? nodeType, string? threadId, string key, string? status, string? payloadJson)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("nodeType", nodeType);
                writer.WriteString("threadId", threadId);
                writer.WriteString("key", key);
                writer.WriteString("status", status);
                writer.WritePropertyName("payload");
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    writer.WriteNullValue();
                }
                else if (IsJsonValue(payloadJson))
                {
                    writer.WriteRawValue(payloadJson);
                }
                else
                {
                    writer.WriteStringValue(payloadJson);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool IsJsonValue(string text)
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>解析编辑器 JSON（非对象/非法 ⇒ false；payload 保留原始 JSON 文本）。</summary>
        public static bool TryParse(
            string? json,
            out string? nodeType, out string? threadId, out string? key, out string? status, out string? payloadJson)
        {
            nodeType = threadId = key = status = payloadJson = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json!);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                nodeType = Str(root, "nodeType");
                threadId = Str(root, "threadId");
                key = Str(root, "key");
                status = Str(root, "status");
                payloadJson = root.TryGetProperty("payload", out var p) && p.ValueKind != JsonValueKind.Undefined
                    ? p.GetRawText()
                    : null;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>nodeType 是否落在写路径白名单（越界 ⇒ 拒写可见化；精确（Ordinal）匹配）。</summary>
        public static bool IsAllowedNodeType(string? nodeType)
            => IsAllowedNodeType(nodeType, ignoreCase: false);

        /// <summary>nodeType 白名单校验（可选大小写不敏感）；越界 ⇒ false。</summary>
        public static bool IsAllowedNodeType(string? nodeType, bool ignoreCase)
            => !string.IsNullOrWhiteSpace(nodeType)
               && NodeTypes.LogicRecordTypes.Contains(
                   nodeType!, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private static string? Str(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
