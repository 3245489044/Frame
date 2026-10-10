using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 对象 ⟷ USharp 数据值 的**直接映射**（路线 B：去掉 JSON 中间层）。
/// <para>
/// 取代旧 <c>UsharpJsonBridge</c>（System.Text.Json / JsonNode 中转）：序列化走「对象 → <see cref="UsharpValue"/> → 文本」，
/// 反序列化走「文本 → <see cref="UsharpValue"/> → 对象」，**全程无 JSON 表示**。
/// </para>
/// <para>口径与旧实现一致：null 属性不写出；标量按 Kind 保真；记录/列表递归。</para>
/// </summary>
public static class UsharpValueMapper
{
    /// <summary>任意 CLR 对象 → USharp 数据值。</summary>
    public static UsharpValue ToValue(object? value)
    {
        switch (value)
        {
            case null:
                return UsharpScalar.Null;
            case UsharpValue v:
                return v;
            case string s:
                return UsharpScalar.Str(s);
            case char c:
                return UsharpScalar.Str(c.ToString());
            case bool b:
                return UsharpScalar.Bool(b);
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return UsharpScalar.Int(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case float or double or decimal:
                return UsharpScalar.Dec(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
            case DateTime dt:
                return UsharpScalar.Str(dt.ToString("O", CultureInfo.InvariantCulture));
            case DateTimeOffset dto:
                return UsharpScalar.Str(dto.ToString("O", CultureInfo.InvariantCulture));
            case Guid g:
                return UsharpScalar.Str(g.ToString("D"));
            case Enum e:
                return UsharpScalar.Str(e.ToString());
            case IDictionary<string, object?> map:
                return new UsharpRecord(map.Select(kv => new KeyValuePair<string, UsharpValue>(kv.Key, ToValue(kv.Value))).ToList());
            case IDictionary dict:
            {
                var fields = new List<KeyValuePair<string, UsharpValue>>();
                foreach (DictionaryEntry entry in dict)
                    fields.Add(new(entry.Key?.ToString() ?? string.Empty, ToValue(entry.Value)));
                return new UsharpRecord(fields);
            }
            case IEnumerable seq:
            {
                var items = new List<UsharpValue>();
                foreach (var item in seq) items.Add(ToValue(item));
                return new UsharpList(items);
            }
            default:
                return ObjectToRecord(value);
        }
    }

    private static UsharpRecord ObjectToRecord(object value)
    {
        var fields = new List<KeyValuePair<string, UsharpValue>>();
        foreach (var p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            var pv = p.GetValue(value);
            if (pv is null) continue; // null 不写出（与旧 WhenWritingNull 口径一致）
            fields.Add(new(p.Name, ToValue(pv)));
        }
        return new UsharpRecord(fields);
    }

    // ===== UsharpValue → 对象 =====

    /// <summary>USharp 数据值 → 目标对象（失败 ⇒ default）。</summary>
    public static T? ToObject<T>(UsharpValue value)
    {
        var converted = ToObject(value, typeof(T));
        return converted is null ? default : (T)converted;
    }

    /// <summary>USharp 数据值 → 指定类型实例（尽力映射；不可映射 ⇒ null）。</summary>
    public static object? ToObject(UsharpValue value, Type target)
    {
        if (target.IsInstanceOfType(value)) return value;
        return value switch
        {
            UsharpScalar scalar => ScalarToTarget(scalar, target),
            UsharpList list => ListToTarget(list, target),
            UsharpRecord record => RecordToTarget(record, target),
            _ => null,
        };
    }

    private static object? ScalarToTarget(UsharpScalar scalar, Type target)
    {
        if (target == typeof(object)) return Uncooked(scalar);
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (scalar.Kind == UsharpScalarKind.Null) return null;
        if (t == typeof(string)) return scalar.Text;
        if (t == typeof(bool)) return string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase);
        if (t.IsEnum) return Enum.Parse(t, scalar.Text, ignoreCase: true);
        if (t == typeof(Guid)) return Guid.Parse(scalar.Text);
        if (t == typeof(DateTime)) return DateTime.Parse(scalar.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(DateTimeOffset)) return DateTimeOffset.Parse(scalar.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(char)) return scalar.Text.Length > 0 ? scalar.Text[0] : '\0';
        try { return Convert.ChangeType(scalar.Text, t, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static object? Uncooked(UsharpScalar scalar) => scalar.Kind switch
    {
        UsharpScalarKind.Null => null,
        UsharpScalarKind.Bool => string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase),
        UsharpScalarKind.Int => long.TryParse(scalar.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : scalar.Text,
        UsharpScalarKind.Decimal => decimal.TryParse(scalar.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : scalar.Text,
        _ => scalar.Text,
    };

    private static object ListToTarget(UsharpList list, Type target)
    {
        if (target.IsArray)
        {
            var elem = target.GetElementType()!;
            var arr = Array.CreateInstance(elem, list.Items.Count);
            for (var i = 0; i < list.Items.Count; i++) arr.SetValue(ToObject(list.Items[i], elem), i);
            return arr;
        }

        var itemType = GetEnumerableElementType(target);
        if (target == typeof(object) || itemType is null) itemType = typeof(object);

        var result = new List<object?>(list.Items.Count);
        foreach (var item in list.Items) result.Add(ToObject(item, itemType));

        if (target == typeof(object) || target.IsAssignableFrom(typeof(List<object?>))) return result;
        var listType = typeof(List<>).MakeGenericType(itemType);
        var typed = Activator.CreateInstance(listType)!;
        var add = listType.GetMethod("Add")!;
        foreach (var item in result) add.Invoke(typed, new[] { item });
        return target.IsAssignableFrom(listType) ? typed : result;
    }

    private static Type? GetEnumerableElementType(Type target)
    {
        if (target.IsGenericType)
        {
            var def = target.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(ICollection<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>))
                return target.GetGenericArguments()[0];
        }
        foreach (var i in target.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return i.GetGenericArguments()[0];
        return null;
    }

    private static object RecordToTarget(UsharpRecord record, Type target)
    {
        if (typeof(IDictionary).IsAssignableFrom(target))
        {
            var args = target.IsGenericType ? target.GetGenericArguments() : new[] { typeof(string), typeof(object) };
            var keyType = args.Length == 2 ? args[0] : typeof(string);
            var valueType = args.Length == 2 ? args[1] : typeof(object);
            if (keyType == typeof(string))
            {
                var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;
                foreach (var f in record.Fields) map[f.Key] = ToObject(f.Value, valueType);
                return map;
            }
        }

        if (target == typeof(object) || target.IsAbstract || target.IsInterface)
            return RecordToDictionary(record);

        var instance = Activator.CreateInstance(target);
        if (instance is null) return RecordToDictionary(record);

        var props = target.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var f in record.Fields)
        {
            var prop = props.FirstOrDefault(p => p.CanWrite &&
                (string.Equals(p.Name, f.Key, StringComparison.Ordinal) || string.Equals(p.Name, f.Key, StringComparison.OrdinalIgnoreCase)));
            if (prop is null) continue;
            prop.SetValue(instance, ToObject(f.Value, prop.PropertyType));
        }
        return instance;
    }

    private static Dictionary<string, object?> RecordToDictionary(UsharpRecord record)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in record.Fields) dict[f.Key] = ToObject(f.Value, typeof(object));
        return dict;
    }
}
