using System;
using System.Collections.Generic;
using System.Text.Json;
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

        /// <summary>业务节点载荷 JSON → 业务节点载荷（解析失败返回空载荷，容错）。</summary>
        public static BusinessNodePayload DecodeBusiness(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<BusinessNodePayload>(json, DocJsonOptions) ?? new BusinessNodePayload();
            }
            catch (JsonException)
            {
                return new BusinessNodePayload();
            }
        }

        /// <summary>业务节点载荷 → JSON。</summary>
        public static string EncodeBusiness(BusinessNodePayload payload)
            => JsonSerializer.Serialize(payload ?? new BusinessNodePayload(), DocJsonOptions);

        /// <summary>载荷 JSON → 文档（解析失败返回空文档，容错）。</summary>
        public static NodeOutputDoc DecodeDoc(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<NodeOutputDoc>(json, DocJsonOptions) ?? new NodeOutputDoc();
            }
            catch (JsonException)
            {
                return new NodeOutputDoc();
            }
        }

        /// <summary>文档 → 载荷 JSON。</summary>
        public static string EncodeDoc(NodeOutputDoc doc)
            => JsonSerializer.Serialize(doc, DocJsonOptions);

        /// <summary>编码单个端口值：非 null 值存「运行时类型 + JSON」；null 值存空位。</summary>
        public static NodeOutputPortValue EncodeValue(object? value)
        {
            if (value is null) return new NodeOutputPortValue { Type = null, Value = null };
            var type = value.GetType();
            try
            {
                return new NodeOutputPortValue { Type = type.AssemblyQualifiedName, Value = JsonSerializer.SerializeToElement(value, type) };
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException)
            {
                // 非可序列化值：降级为字符串表达，仍保留数据（读取侧还原为 string）。
                return new NodeOutputPortValue
                {
                    Type = typeof(string).AssemblyQualifiedName,
                    Value = JsonSerializer.SerializeToElement(value.ToString() ?? string.Empty),
                };
            }
        }

        /// <summary>解码单个端口值：按声明类型还原；类型不可解析 / 反序列化失败回退为 JsonElement。</summary>
        public static object? DecodeValue(NodeOutputPortValue pv)
        {
            if (pv.Value is null) return null;
            var elem = pv.Value.Value;
            if (pv.Type is null) return null;
            var type = Type.GetType(pv.Type);
            if (type is null) return elem.Clone();
            try
            {
                return elem.Deserialize(type);
            }
            catch (JsonException)
            {
                return elem.Clone();
            }
        }
    }
}
