using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Plate.DslMethod.Usharp;
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
    // ===== 成员计划缓存（每类型编译一次的委托；运行时零逐次反射调用）=====

    private static readonly ConcurrentDictionary<Type, TypePlan> Plans = new();

    private sealed class TypePlan
    {
        public ConstructorInfo? Ctor;
        public ParameterInfo[]? CtorParams;
        public MemberPlan[] Members = Array.Empty<MemberPlan>();
        public readonly Dictionary<string, MemberPlan> ByName = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MemberPlan
    {
        public string Name = string.Empty;
        public Type Type = typeof(object);
        public PropertyInfo? Property;
        public Func<object, object?>? Get;
        public Action<object, object?>? Set;
    }

    /// <summary>只读对象递归回填的最大深度（防异常模型引入环）。</summary>
    private const int MaxPopulateDepth = 8;

    private static TypePlan GetPlan(Type type) => Plans.GetOrAdd(type, BuildPlan);

    /// <summary>构建类型计划：属性 getter/setter 编译为委托（Expression.Lambda.Compile），并缓存。</summary>
    private static TypePlan BuildPlan(Type type)
    {
        var plan = new TypePlan
        {
            Ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null),
        };

        // 位置记录（record 主构造，无参构造）：取参数最多的公共构造，按参数名从记录字段绑定。
        if (plan.Ctor is null)
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault(c => c.GetParameters().Length > 0);
            if (ctor is not null) { plan.Ctor = ctor; plan.CtorParams = ctor.GetParameters(); }
        }

        var members = new List<MemberPlan>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0) continue;

            var mp = new MemberPlan { Name = prop.Name, Type = prop.PropertyType, Property = prop };

            if (prop.GetGetMethod(nonPublic: false) is not null)
            {
                var o = Expression.Parameter(typeof(object), "o");
                var body = Expression.Convert(Expression.Property(Expression.Convert(o, type), prop), typeof(object));
                mp.Get = Expression.Lambda<Func<object, object?>>(body, o).Compile();
            }

            if (prop.GetSetMethod(nonPublic: false) is not null)
            {
                var o = Expression.Parameter(typeof(object), "o");
                var v = Expression.Parameter(typeof(object), "v");
                var assign = Expression.Assign(
                    Expression.Property(Expression.Convert(o, type), prop),
                    Expression.Convert(v, prop.PropertyType));
                mp.Set = Expression.Lambda<Action<object, object?>>(assign, o, v).Compile();
            }

            members.Add(mp);
            plan.ByName[prop.Name] = mp;
        }

        plan.Members = members.ToArray();
        return plan;
    }

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
    {
        // object 目标必须「拆包」为 CLR 值；UsharpScalar/UsharpRecord 本身也是 object，
        // 不能走 IsInstanceOfType 短路（否则 Data / 字典 object 值会残留 Usharp 节点）。
        if (target != typeof(object) && target.IsInstanceOfType(value)) return value;
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

        var plan = GetPlan(target);
        if (plan.Ctor is null) return RecordToDictionary(record);

        // 无参构造：逐属性注入。
        if (plan.CtorParams is null or { Length: 0 })
        {
            var instance = plan.Ctor.Invoke(null);
            if (instance is null) return RecordToDictionary(record);
            foreach (var f in record.Fields)
            {
                if (!plan.ByName.TryGetValue(f.Key, out var m)) continue;
                ApplyMember(instance, m, f.Value, depth: 0);
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
                ctorArgs[i] = ToObject(f.Value, p.ParameterType);
                break;
            }
        }
        return plan.Ctor.Invoke(ctorArgs) ?? (object)RecordToDictionary(record);
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
    private static void ApplyMember(object instance, MemberPlan member, UsharpValue value, int depth)
    {
        if (member.Set is not null)
        {
            member.Set(instance, ToObject(value, member.Type));
            return;
        }

        if (member.Property?.SetMethod is { } setter)
        {
            try
            {
                setter.Invoke(instance, new[] { ToObject(value, member.Type) });
                return;
            }
            catch
            {
                // init-only / 集合初始化器等不可在现有实例上写回 ⇒ 退回就地回填。
            }
        }

        if (member.Get is null) return;
        var current = member.Get(instance);
        if (current is null) return;
        PopulateInto(current, value, member.Type, depth + 1);
    }

    /// <summary>
    /// 把 USharp 值就地回填到只读成员（字典 / 列表 / 只读组合对象）。
    /// 只处理「容器或可递归对象」；标量只读属性（计算属性）无写入语义，保持原状。
    /// </summary>
    private static void PopulateInto(object target, UsharpValue value, Type declaredType, int depth)
    {
        if (depth > MaxPopulateDepth) return;

        switch (value)
        {
            case UsharpRecord record:
                // ① 通用字典视图（含 DataSpace 的显式 IDictionary<string,object> 实现）。
                if (target is IDictionary<string, object> objectMap)
                {
                    foreach (var f in record.Fields)
                        objectMap[f.Key] = ToObject(f.Value, typeof(object))!;
                    return;
                }

                // ② 非泛型字典（Dictionary<string,string> 等）。
                if (target is IDictionary map)
                {
                    var valueType = GetDictionaryValueType(declaredType);
                    foreach (var f in record.Fields)
                        map[f.Key] = ToObject(f.Value, valueType);
                    return;
                }

                // ③ 只读组合对象：递归回填其成员（Record / LoopState / Budget / Token / Flag…）。
                var plan = GetPlan(target.GetType());
                if (plan.Ctor is null) return;
                foreach (var f in record.Fields)
                {
                    if (!plan.ByName.TryGetValue(f.Key, out var member)) continue;
                    ApplyMember(target, member, f.Value, depth + 1);
                }
                return;

            case UsharpList list:
                if (target is IList listTarget)
                {
                    var itemType = GetEnumerableElementType(declaredType) ?? typeof(object);
                    listTarget.Clear();
                    foreach (var item in list.Items)
                        listTarget.Add(ToObject(item, itemType));
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

    private static Dictionary<string, object?> RecordToDictionary(UsharpRecord record)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in record.Fields) dict[f.Key] = ToObject(f.Value, typeof(object));
        return dict;
    }
}
