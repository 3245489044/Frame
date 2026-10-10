using System.Globalization;
using System.Text.Json.Nodes;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// JSON 节点 ↔ USharp 数据值 的桥接（单一实现面）：
/// 对象映射仍复用 System.Text.Json（<see cref="JsonNode"/>），USharp 只承担**文本格式**；
/// 由此以最小风险获得「任意对象 ⟷ USharp 文本」的全通用编解码。
/// </summary>
public static class UsharpJsonBridge
{
    /// <summary>JSON 节点 → USharp 值。</summary>
    public static UsharpValue ToValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return UsharpScalar.Null;
            case JsonObject obj:
            {
                var fields = new List<KeyValuePair<string, UsharpValue>>();
                foreach (var kv in obj)
                    fields.Add(new KeyValuePair<string, UsharpValue>(kv.Key, ToValue(kv.Value)));
                return new UsharpRecord(fields);
            }
            case JsonArray arr:
            {
                var items = new List<UsharpValue>();
                foreach (var item in arr) items.Add(ToValue(item));
                return new UsharpList(items);
            }
            default:
            {
                var value = node.AsValue();
                if (value.TryGetValue<string>(out var s)) return UsharpScalar.Str(s);
                if (value.TryGetValue<bool>(out var b)) return UsharpScalar.Bool(b);
                if (value.TryGetValue<long>(out var l)) return UsharpScalar.Int(l);
                if (value.TryGetValue<decimal>(out var dec)) return UsharpScalar.Dec(dec);
                if (value.TryGetValue<double>(out var d)) return UsharpScalar.Dec((decimal)d);
                return UsharpScalar.Str(value.ToJsonString());
            }
        }
    }

    /// <summary>USharp 值 → JSON 节点。</summary>
    public static JsonNode? ToJson(UsharpValue value)
    {
        switch (value)
        {
            case UsharpRecord record:
            {
                var obj = new JsonObject();
                foreach (var f in record.Fields) obj[f.Key] = ToJson(f.Value);
                return obj;
            }
            case UsharpList list:
            {
                var arr = new JsonArray();
                foreach (var item in list.Items) arr.Add(ToJson(item));
                return arr;
            }
            default:
            {
                var scalar = (UsharpScalar)value;
                return scalar.Kind switch
                {
                    UsharpScalarKind.Null => null,
                    UsharpScalarKind.String => JsonValue.Create(scalar.Text),
                    UsharpScalarKind.Bool => JsonValue.Create(string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase)),
                    UsharpScalarKind.Int => JsonValue.Create(long.Parse(scalar.Text, CultureInfo.InvariantCulture)),
                    UsharpScalarKind.Decimal => JsonValue.Create(decimal.Parse(scalar.Text, CultureInfo.InvariantCulture)),
                    _ => JsonValue.Create(scalar.Text),
                };
            }
        }
    }
}
