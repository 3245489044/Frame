
using System.Text.Json;
using System.Text.Json.Nodes;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Runtime
{
    /// <summary>
    /// 转换 IR ↔ <see cref="JsonNode"/> 双向桥（运行期唯一换算点）。
    /// 正向：IR → <see cref="JsonNode"/>，随后由 <c>Plate.JsonMethod</c> 负责实际序列化（平台 JSON 单一出口）。
    /// 反向：既有 JSON 读回 IR，供「展开产物 vs 现行 JSON 语义等价」对拍复用内核规则/发射器。
    /// 键序：<see cref="JsonObject"/> 与 <see cref="ConversionObject"/> 均保持插入序 → 往返不重排。
    /// </summary>
    public static class ConversionNodeJsonNodeBridge
    {
        /// <summary>IR → JsonNode（JSON null 表示为 null 引用）。</summary>
        /// <exception cref="JsonException"><see cref="ConversionRaw"/> 片段非法时抛出。</exception>
        public static JsonNode? ToJsonNode(ConversionNode? node)
        {
            switch (node)
            {
                case null:
                    return null;

                case ConversionObject obj:
                {
                    var jsonObject = new JsonObject();
                    foreach (var member in obj.Members)
                    {
                        jsonObject[member.Key] = ToJsonNode(member.Value);
                    }
                    return jsonObject;
                }

                case ConversionArray array:
                {
                    var jsonArray = new JsonArray();
                    foreach (var item in array.Items)
                    {
                        jsonArray.Add(ToJsonNode(item));
                    }
                    return jsonArray;
                }

                case ConversionScalar scalar:
                    return ToJsonValue(scalar);

                case ConversionRaw raw:
                    // 原样片段必须已是合法 JSON 才能进入 JsonNode 世界；非法即抛（由发射器转诊断）
                    return JsonNode.Parse(raw.Text);

                default:
                    throw new JsonException($"未知 IR 节点种类：{node.Kind}");
            }
        }

        /// <summary>JsonNode → IR（null → JSON null 标量）。</summary>
        public static ConversionNode FromJsonNode(JsonNode? node)
        {
            switch (node)
            {
                case null:
                    return ConversionScalar.Null;

                case JsonObject jsonObject:
                {
                    var result = new ConversionObject();
                    foreach (var pair in jsonObject)
                    {
                        result.Add(pair.Key, FromJsonNode(pair.Value));
                    }
                    return result;
                }

                case JsonArray jsonArray:
                {
                    var result = new ConversionArray();
                    foreach (var item in jsonArray)
                    {
                        result.Add(FromJsonNode(item));
                    }
                    return result;
                }

                case JsonValue jsonValue:
                    return FromJsonValue(jsonValue);

                default:
                    return ConversionScalar.Of(node.ToJsonString());
            }
        }

        private static JsonNode? ToJsonValue(ConversionScalar scalar)
        {
            switch (scalar.ScalarKind)
            {
                case ConversionScalarKind.Null:
                    return null;

                case ConversionScalarKind.Boolean:
                    return JsonValue.Create(scalar.Value is bool flag && flag);

                case ConversionScalarKind.Integer:
                    return JsonValue.Create(scalar.Value is long number ? number : 0L);

                case ConversionScalarKind.Number:
                    return JsonValue.Create(scalar.Value is double value ? value : 0.0);

                case ConversionScalarKind.String:
                    return JsonValue.Create(scalar.Value as string ?? string.Empty);

                default:
                    return null;
            }
        }

        private static ConversionNode FromJsonValue(JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolean)) return ConversionScalar.Of(boolean);
            if (value.TryGetValue<long>(out var integer)) return ConversionScalar.Of(integer);
            if (value.TryGetValue<double>(out var number)) return ConversionScalar.Of(number);
            if (value.TryGetValue<decimal>(out var decimalValue)) return ConversionScalar.Of((double)decimalValue);
            if (value.TryGetValue<string>(out var text)) return ConversionScalar.Of(text);

            return ConversionScalar.Of(value.ToJsonString());
        }
    }
}
