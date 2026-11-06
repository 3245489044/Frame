using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrameworkAbstractions.Converters;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
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
    // ===== 成员计划缓存（通用反射绑定下沉 Share；本工程只提供「忽略标记」过滤 + 回填深度语义）=====

    private static readonly ReflectionPlanCache Plans = new(
        static prop => !prop.IsDefined(typeof(UsharpIgnoreAttribute), inherit: false));

    /// <summary>只读对象递归回填的最大深度（防异常模型引入环）。</summary>
    private const int MaxPopulateDepth = 8;

    private static ReflectionBindingPlan GetPlan(Type type) => Plans.Get(type);

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
        var plan = GetPlan(value.GetType());
        var fields = new List<KeyValuePair<string, UsharpValue>>(plan.Members.Length);
        foreach (var m in plan.Members)
        {
            if (m.Get is null) continue;
            var pv = m.Get(value);
            if (pv is null) continue; // null 不写出（与旧 WhenWritingNull 口径一致）
            fields.Add(new(m.Name, ToValue(pv)));
        }
        return new UsharpRecord(fields);
    }

    // ===== UsharpValue → 对象 =====

    /// <summary>USharp 数据值 → 目标对象（失败 ⇒ default）。</summary>
    public static T? ToObject<T>(UsharpValue value)
    {
        var converted = ToObject(value, typeof(T));
        return converted is T typed ? typed : default;
    }

    /// <summary>USharp 数据值 → 指定类型实例（尽力映射；不可映射 ⇒ null）。</summary>
    public static object? ToObject(UsharpValue value, Type target)
        => ToObject(value, target, failures: null, path: string.Empty, depth: 0);

    /// <summary>
    /// 失败可见的映射入口：返回 value + 失败报告（不抛、不静默吞）。
    /// </summary>
    /// <param name="value">USharp 值。</param>
    /// <typeparam name="T">目标类型。</typeparam>
    public static UsharpTry<T> TryToObject<T>(UsharpValue value)
    {
        var failures = new List<UsharpFailure>();
        var converted = ToObject(value, typeof(T), failures, typeof(T).Name, depth: 0);
        if (converted is T typed && failures.Count == 0) return UsharpTry<T>.Success(typed);

        var reason = failures.Count == 0
            ? $"无法映射为 {typeof(T).Name}"
            : string.Join("; ", failures.Select(f => $"{f.Path}:{f.Reason}"));
        return UsharpTry<T>.Failure(reason);
    }

    private static object? ToObject(
        UsharpValue value, Type target, List<UsharpFailure>? failures, string path, int depth)
    {
        // 深度截断可见化（E-4）：嵌套记录递归须有上限，超限不再静默继续。
        if (depth > MaxPopulateDepth)
        {
            failures?.Add(new UsharpFailure($"depth-limit({MaxPopulateDepth})", path));
            return null;
        }

        // object 目标必须「拆包」为 CLR 值；UsharpScalar/UsharpRecord 本身也是 object，
        // 不能走 IsInstanceOfType 短路（否则 Data / 字典 object 值会残留 Usharp 节点）。
        if (target != typeof(object) && target.IsInstanceOfType(value)) return value;
        return value switch
        {
            UsharpScalar scalar => ScalarToTarget(scalar, target, failures, path),
            UsharpList list => ListToTarget(list, target, failures, path, depth),
            UsharpRecord record => RecordToTarget(record, target, failures, path, depth),
            _ => null,
        };
    }

    private static object? ScalarToTarget(
        UsharpScalar scalar, Type target, List<UsharpFailure>? failures, string path)
    {
        if (target == typeof(object)) return Uncooked(scalar);
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (scalar.Kind == UsharpScalarKind.Null) return null;
        if (t == typeof(string)) return scalar.Text;
        if (t == typeof(bool))
            return UsharpLineReader.TryBool(scalar.Text, UsharpBoolLexicon.Extended, out var b)
                ? b
                : Fail($"bool-invalid('{scalar.Text}')", path, failures);
        if (t.IsEnum)
        {
            if (Enum.TryParse(t, scalar.Text, ignoreCase: true, out var parsed) && Enum.IsDefined(t, parsed))
                return parsed;
            return Fail($"enum-invalid('{scalar.Text}')", path, failures);
        }

        if (t == typeof(Guid))
            return Guid.TryParse(scalar.Text, out var guid) ? guid : Fail($"guid-invalid('{scalar.Text}')", path, failures);
        if (t == typeof(DateTime))
            return DateTime.TryParse(scalar.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : Fail($"datetime-invalid('{scalar.Text}')", path, failures);
        if (t == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(scalar.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                ? dto
                : Fail($"datetimeoffset-invalid('{scalar.Text}')", path, failures);
        if (t == typeof(char)) return scalar.Text.Length > 0 ? scalar.Text[0] : '\0';

        try
        {
            return Convert.ChangeType(scalar.Text, t, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            return Fail($"convert-failed({ex.GetType().Name})", path, failures);
        }
    }

    private static object? Fail(string reason, string path, List<UsharpFailure>? failures)
    {
        failures?.Add(new UsharpFailure(reason, path));
        return null;
    }

    private static object? Uncooked(UsharpScalar scalar) => scalar.Kind switch
    {
        UsharpScalarKind.Null => null,
        UsharpScalarKind.Bool => string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase),
        UsharpScalarKind.Int => long.TryParse(scalar.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : scalar.Text,
        UsharpScalarKind.Decimal => decimal.TryParse(scalar.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : scalar.Text,
        _ => scalar.Text,
    };

    private static object ListToTarget(
        UsharpList list, Type target, List<UsharpFailure>? failures, string path, int depth)
    {
        if (target.IsArray)
        {
            var elem = target.GetElementType()!;
            var arr = Array.CreateInstance(elem, list.Items.Count);
            for (var i = 0; i < list.Items.Count; i++)
                arr.SetValue(ToObject(list.Items[i], elem, failures, $"{path}[{i}]", depth + 1), i);
            return arr;
        }

        var itemType = GetEnumerableElementType(target);
        if (target == typeof(object) || itemType is null) itemType = typeof(object);

        var result = new List<object?>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
            result.Add(ToObject(list.Items[i], itemType, failures, $"{path}[{i}]", depth + 1));

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

    private static object RecordToTarget(
        UsharpRecord record, Type target, List<UsharpFailure>? failures, string path, int depth)
    {
        if (typeof(IDictionary).IsAssignableFrom(target))
        {
            var args = target.IsGenericType ? target.GetGenericArguments() : new[] { typeof(string), typeof(object) };
            var keyType = args.Length == 2 ? args[0] : typeof(string);
            var valueType = args.Length == 2 ? args[1] : typeof(object);
            if (keyType == typeof(string))
            {
                var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;
                foreach (var f in record.Fields)
                    map[f.Key] = ToObject(f.Value, valueType, failures, $"{path}.{f.Key}", depth + 1);
                return map;
            }
        }

        if (target == typeof(object) || target.IsAbstract || target.IsInterface)
            return RecordToDictionary(record, failures, path);

        var plan = GetPlan(target);
        if (plan.Ctor is null) return RecordToDictionary(record, failures, path);

        // 无参构造：逐属性注入。
        if (plan.CtorParams is null or { Length: 0 })
        {
            var instance = plan.Ctor.Invoke(null);
            if (instance is null) return RecordToDictionary(record, failures, path);
            foreach (var f in record.Fields)
            {
                if (!plan.ByName.TryGetValue(f.Key, out var m)) continue;
                ApplyMember(instance, m, f.Value, depth, failures, $"{path}.{m.Name}");
            }
            return instance;
        }

        // 位置记录：按参数名从记录字段绑定后经构造注入。
        var ctorArgs = new object?[plan.CtorParams.Length];
        for (var i = 0; i < plan.CtorParams.Length; i++)
        {
            var p = plan.CtorParams[i];
            foreach (var f in record.Fields)
            {
                if (!string.Equals(f.Key, p.Name, StringComparison.OrdinalIgnoreCase)) continue;
                ctorArgs[i] = ToObject(f.Value, p.ParameterType, failures, $"{path}.{p.Name}", depth + 1);
                break;
            }
        }
        return plan.Ctor.Invoke(ctorArgs) ?? (object)RecordToDictionary(record, failures, path);
    }

    /// <summary>
    /// 把一条记录字段写入目标成员。**写回优先级**：
    /// ① 公共 setter → 直接赋值；
    /// ② 非公共 setter（private set / init，如 FailureBudget.Level、ProgressToken.Value、ControlFlag.Halt）
    ///    → 反射恢复（持久化回读语义，必须把落盘值还原）；
    /// ③ 只读属性（无任何 setter，如 AgentStatus / Data / AvailableStrategyIds / RecentTransitions
    ///    以及 Record / LoopState 这类只读组合对象）→ 对**现有实例**就地回填。
    /// <para>旧实现只做 ①，导致「只读集合/组合对象」在 USharp round-trip 后丢失（UI 显示执行体 Unknown 的根因）。</para>
    /// </summary>
    private static void ApplyMember(
        object instance, ReflectionBindingPlan.Member member, UsharpValue value, int depth,
        List<UsharpFailure>? failures, string path)
    {
        var setterFailed = false;

        if (member.Set is not null)
        {
            try
            {
                member.Set(instance, ToObject(value, member.Type, failures, path, depth + 1));
                return;
            }
            catch (Exception ex)
            {
                // 编译委托抛错（如 setter 体抛异常）⇒ 失败可见，不再上抛中断整次映射。
                failures?.Add(new UsharpFailure($"setter-failed({ex.GetType().Name})", path));
                setterFailed = true;
            }
        }

        if (member.Property?.SetMethod is { } setter)
        {
            try
            {
                setter.Invoke(instance, new[] { ToObject(value, member.Type, failures, path, depth + 1) });
                return;
            }
            catch (Exception ex)
            {
                // init-only / 集合初始化器等不可在现有实例上写回 ⇒ 退回就地回填（失败可见）。
                if (!setterFailed) failures?.Add(new UsharpFailure($"setter-failed({ex.GetType().Name})", path));
            }
        }

        if (member.Get is null) return;
        var current = member.Get(instance);
        if (current is null) return;
        PopulateInto(current, value, member.Type, depth + 1, failures, path);
    }

    /// <summary>
    /// 把 USharp 值就地回填到只读成员（字典 / 列表 / 只读组合对象）。
    /// 只处理「容器或可递归对象」；标量只读属性（计算属性）无写入语义，保持原状。
    /// </summary>
    private static void PopulateInto(
        object target, UsharpValue value, Type declaredType, int depth,
        List<UsharpFailure>? failures, string path)
    {
        if (depth > MaxPopulateDepth)
        {
            // 深度截断不再静默（E-4）：可见化为 depth-limit。
            failures?.Add(new UsharpFailure($"depth-limit({MaxPopulateDepth})", path));
            return;
        }

        switch (value)
        {
            case UsharpRecord record:
                // ① 通用字典视图（含 DataSpace 的显式 IDictionary<string,object> 实现）。
                if (target is IDictionary<string, object> objectMap)
                {
                    foreach (var f in record.Fields)
                        objectMap[f.Key] = ToObject(f.Value, typeof(object), failures, $"{path}.{f.Key}", depth + 1)!;
                    return;
                }

                // ② 非泛型字典（Dictionary<string,string> 等）。
                if (target is IDictionary map)
                {
                    var valueType = GetDictionaryValueType(declaredType);
                    foreach (var f in record.Fields)
                        map[f.Key] = ToObject(f.Value, valueType, failures, $"{path}.{f.Key}", depth + 1);
                    return;
                }

                // ③ 只读组合对象：递归回填其成员（Record / LoopState / Budget / Token / Flag…）。
                var plan = GetPlan(target.GetType());
                if (plan.Ctor is null) return;
                foreach (var f in record.Fields)
                {
                    if (!plan.ByName.TryGetValue(f.Key, out var member)) continue;
                    ApplyMember(target, member, f.Value, depth + 1, failures, $"{path}.{member.Name}");
                }
                return;

            case UsharpList list:
                if (target is IList listTarget)
                {
                    var itemType = GetEnumerableElementType(declaredType) ?? typeof(object);
                    listTarget.Clear();
                    var index = 0;
                    foreach (var item in list.Items)
                        listTarget.Add(ToObject(item, itemType, failures, $"{path}[{index++}]", depth + 1));
                }
                return;
        }
    }

    /// <summary>字典值类型（Dictionary&lt;K,V&gt; / IDictionary&lt;K,V&gt; / IReadOnlyDictionary&lt;K,V&gt;）；不可判定 ⇒ object。</summary>
    private static Type GetDictionaryValueType(Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                return type.GetGenericArguments()[1];
        }

        foreach (var i in type.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            var def = i.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                return i.GetGenericArguments()[1];
        }

        return typeof(object);
    }

    private static Dictionary<string, object?> RecordToDictionary(
        UsharpRecord record, List<UsharpFailure>? failures = null, string path = "")
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in record.Fields)
            dict[f.Key] = ToObject(f.Value, typeof(object), failures, $"{path}.{f.Key}", depth: 0);
        return dict;
    }
}
