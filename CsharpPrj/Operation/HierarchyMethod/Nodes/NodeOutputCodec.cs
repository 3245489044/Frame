using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Plate.JsonMethod.Serialization;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes {
    /// <summary>节点输出端口的 JSON 载荷形态（ur_nodes NodeType=output 记录承载，每个节点一条记录）。</summary>
    public sealed class NodeOutputDoc
    {
        /// <summary>端口值字典（port → 类型保真值）。</summary>
        public Dictionary<string, NodeOutputPortValue> Ports { get; set; } = new(StringComparer.Ordinal);

        /// <summary>节点输出可见性（<see cref="OutputVisibility"/> 名称；缺省 WorkflowLocal）。</summary>
        public string Visibility { get; set; } = nameof(OutputVisibility.WorkflowLocal);

        // ---- 节点信号元数据（「节点 + 信号驱动」的最近/价值/区域评判依据；可空 = 未标注）----
        /// <summary>功能模块（execution/task/plan/decide/...）。</summary>
        public string? Module { get; set; }

        /// <summary>区域（由 module × taskKey 派生，如 task.proj-x）。</summary>
        public string? Region { get; set; }

        /// <summary>节点类别（execution_node/task_node/...）。</summary>
        public string? NodeType { get; set; }

        /// <summary>新鲜度（落库时间戳，Unix 秒；缺省 0 = 用落库时序推断最近）。</summary>
        public long Freshness { get; set; }

        /// <summary>价值评分（0..1；可空）。</summary>
        public double? ValueScore { get; set; }

        /// <summary>输出摘要（正文截断摘要，免读正文；可空 = 未生成）。</summary>
        public string? OutputDigest { get; set; }

        /// <summary>显式焦点标记（恒最前）。</summary>
        public bool Sticky { get; set; }

        /// <summary>显式优先级（越大越前）。</summary>
        public int Priority { get; set; }
    }

    /// <summary>单个端口值：类型名 + 序列化值（类型保真往返，支撑强类型读取）。</summary>
    public sealed class NodeOutputPortValue
    {
        /// <summary>运行时类型（AssemblyQualifiedName）；null 表示值为 null。</summary>
        public string? Type { get; set; }

        /// <summary>序列化后的值（JSON 元素）；null 表示值为 null。</summary>
        public JsonElement? Value { get; set; }
    }

    /// <summary>
    /// 业务节点唯一载荷（输出并入业务节点，消除 NodeType=output 独立记录双写）：
    /// Symbol（节点标识）+ Outputs（节点输出端口，复用 <see cref="NodeOutputDoc"/>）。
    /// 业务节点 PayloadJson 即此形态；NodeOutputStore 读写输出、NodeExecutionService 写标识/关系，两者读-改-写互不覆盖。
    /// </summary>
    public sealed class BusinessNodePayload
    {
        public string? Symbol { get; set; }

        public NodeOutputDoc? Outputs { get; set; }
    }

    /// <summary>
    /// 节点输出值编解码：把任意 object 端口值编码为「类型名 + JSON」、按类型解码还原。
    /// 类型不可解析 / 反序列化失败时回退为 JsonElement（保数据不抛异常，读取侧不因单个端口降级而中断）。
    /// 中立承载于 Agent.Contract（纯 JSON 编解码，无实现依赖），供 Core 执行/存储侧与 Agent.Reference 投影侧共用。
    /// </summary>
    public static class NodeOutputCodec
    {
        // 平台统一 JSON 框架 Compact 底座 + 覆盖保持既有存储形态（PascalCase 属性名、写 null、单行紧凑），
        // 仅编码升级为 UnsafeRelaxedJsonEscaping（非 ASCII 直出，消除 \uXXXX 转义污染存储）。
        private static readonly JsonSerializerOptions DocJsonOptions = JsonSerializerFactory.Create(
            JsonContext.Compact,
            o => { o.PropertyNamingPolicy = null; o.DefaultIgnoreCondition = JsonIgnoreCondition.Never; });

        /// <summary>业务节点载荷 JSON → 业务节点载荷（解析失败返回空载荷，容错）。编码动作单源 = Plate.JsonMethod。</summary>
        public static BusinessNodePayload DecodeBusiness(string json)
        {
            try
            {
                return JsonSerializationService.Instance.Deserialize<BusinessNodePayload>(json, DocJsonOptions) ?? new BusinessNodePayload();
            }
            catch (JsonException)
            {
                return new BusinessNodePayload();
            }
        }

        /// <summary>业务节点载荷 → JSON（编码动作单源 = Plate.JsonMethod）。</summary>
        public static string EncodeBusiness(BusinessNodePayload payload)
            => JsonSerializationService.Instance.Serialize(payload ?? new BusinessNodePayload(), DocJsonOptions);

        /// <summary>文档 → 载荷 JSON（NodeOutputDoc 独立承载场景，如投影/测试装配）。</summary>
        public static string EncodeDoc(NodeOutputDoc doc)
            => JsonSerializationService.Instance.Serialize(doc, DocJsonOptions);

        /// <summary>
        /// 外部载荷 → ur_nodes 记录载荷 JSON（DP-2 单一构造入口）：输出文档形态经本 Codec 统一落库 ——
        /// 已含 <c>Outputs</c>（业务节点载荷）→ 规范化重编码（补 Symbol）；已含 <c>Ports</c>（输出文档）→ 包成业务节点载荷；
        /// 其余任意 JSON / 非 JSON（通用外部透传口：storage-bridge / MCP / UI 函数库）→ **原样返回**
        /// （读侧为 node.read / UI 原始载荷展示，不进执行链端口读回）。空载荷 → 空业务载荷。
        /// </summary>
        public static string EncodeExternal(string? payloadJson, string? symbol)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return EncodeBusiness(new BusinessNodePayload { Symbol = symbol });
            try
            {
                // 解析单源 = JsonMethod（原 JsonDocument.Parse 手写探针 → JsonSerializationService.ParseJsonNode）。
                if (JsonSerializationService.Instance.ParseJsonNode(payloadJson) is not JsonObject root) return payloadJson;
                if (root.ContainsKey(nameof(BusinessNodePayload.Outputs)))
                {
                    var business = DecodeBusiness(payloadJson);
                    business.Symbol ??= symbol;
                    return EncodeBusiness(business);
                }
                if (root.ContainsKey(nameof(NodeOutputDoc.Ports)))
                {
                    var doc = JsonSerializationService.Instance.Deserialize<NodeOutputDoc>(payloadJson, DocJsonOptions);
                    if (doc is not null)
                        return EncodeBusiness(new BusinessNodePayload { Symbol = symbol, Outputs = doc });
                }
            }
            catch (JsonException)
            {
                return payloadJson; // 非 JSON 载荷：原样保留（不抛、不改写外部契约）
            }
            return payloadJson;
        }

        /// <summary>编码单个端口值（薄壳）：类型保真编解码单源 = <see cref="TypedJsonValueCodec.Encode"/>。</summary>
        public static NodeOutputPortValue EncodeValue(object? value)
        {
            var typed = TypedJsonValueCodec.Encode(value);
            return new NodeOutputPortValue { Type = typed.Type, Value = typed.Value };
        }

        /// <summary>解码单个端口值（薄壳）：类型保真编解码单源 = <see cref="TypedJsonValueCodec.Decode"/>。</summary>
        public static object? DecodeValue(NodeOutputPortValue pv)
            => TypedJsonValueCodec.Decode(new TypedJsonValue { Type = pv.Type, Value = pv.Value });
    }
}
