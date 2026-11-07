using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Modules.Node.Frame;

/// <summary>
/// 帧字段值 → JSON 节点（**落库序列化唯一实现**，与读侧 <see cref="NodeJson.ValueToText"/> 同源成对）。
/// <para>
/// 标量按原类型落值（<c>string/bool/整型/浮点</c>）；枚举取其名；时刻取 ISO-8601；
/// **字典按内容落成 JSON 对象、集合落成 JSON 数组**（递归）；其余对象才退回 <c>ToString()</c>。
/// </para>
/// <para>
/// 纪律：字典/集合**禁止**经 <c>ToString()</c> 落库 —— 那会把结构事实降级成类型名
/// （如 <c>System.Collections.Generic.Dictionary`2[...]</c>），读侧无法还原、也无法消费。
/// </para>
/// </summary>
public static class FrameValueJson
{
    /// <summary>帧字段值 → JSON 节点（累加器写侧与读侧字段口径共用）。</summary>
    public static JsonNode? ToNode(object value)
    {
        switch (value)
        {
            case string text: return JsonValue.Create(text);
            case bool flag: return JsonValue.Create(flag);
            case int number: return JsonValue.Create(number);
            case long number: return JsonValue.Create(number);
            case double number: return JsonValue.Create(number);
            case float number: return JsonValue.Create(number);
            case decimal number: return JsonValue.Create(number);
            case short number: return JsonValue.Create(number);
            case byte number: return JsonValue.Create(number);
            case Enum enumValue: return JsonValue.Create(enumValue.ToString());
            case DateTimeOffset stamp: return JsonValue.Create(stamp.ToString("O"));
            case DateTime stamp: return JsonValue.Create(stamp.ToString("O"));
        }

        if (TryDictionaryEntries(value, out var entries))
        {
            var obj = new JsonObject();
            foreach (var pair in entries)
            {
                obj[pair.Key] = pair.Value is null ? null : ToNode(pair.Value);
            }

            return obj;
        }

        if (value is IEnumerable sequence)
        {
            var array = new JsonArray();
            foreach (var item in sequence)
            {
                array.Add(item is null ? null : ToNode(item));
            }

            return array;
        }

        return JsonValue.Create(value.ToString());
    }

    /// <summary>
    /// 字典探测（字符串键）：先走 <see cref="IDictionary"/>（<see cref="Dictionary{TKey,TValue}"/>/只读包装等），
    /// 再按泛型接口 <see cref="IReadOnlyDictionary{TKey,TValue}"/>/<see cref="IDictionary{TKey,TValue}"/> 兜底
    /// （覆盖不可变字典等不实现非泛型接口的容器）。
    /// </summary>
    private static bool TryDictionaryEntries(
        object value, out IEnumerable<KeyValuePair<string, object?>> entries)
    {
        entries = Array.Empty<KeyValuePair<string, object?>>();

        if (value is IDictionary dictionary)
        {
            var mapped = new List<KeyValuePair<string, object?>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                mapped.Add(new KeyValuePair<string, object?>(
                    entry.Key?.ToString() ?? string.Empty, entry.Value));
            }

            entries = mapped;
            return true;
        }

        var contract = value.GetType().GetInterfaces().FirstOrDefault(type =>
            type.IsGenericType
            && type.GetGenericArguments().Length == 2
            && type.GetGenericArguments()[0] == typeof(string)
            && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                || type.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
        if (contract is null) return false;

        var keys = contract.GetProperty("Keys")!.GetValue(value) as IEnumerable;
        var indexer = contract.GetProperty("Item")!;
        var items = new List<KeyValuePair<string, object?>>();
        foreach (var key in keys!)
        {
            items.Add(new KeyValuePair<string, object?>(
                key.ToString() ?? string.Empty, indexer.GetValue(value, new[] { key })));
        }

        entries = items;
        return true;
    }
}
