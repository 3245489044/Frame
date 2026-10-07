using System.Reflection;
using System.Text.Json;
using DataCenter.Protocol.Standards;
using DataCenter.Protocol.Standards.Descriptors;
using MemberDescriptor = DataCenter.Protocol.Standards.Descriptors.MemberDescriptor;

namespace DataCenter.Standards;

/// <summary>
/// 通用读取器引擎（描述符单源化 §4.1；取代 T1/M1 的**常规层**手写分支）：
/// 由 <see cref="LayerDescriptor"/> 驱动，按 <see cref="MemberKind"/> 分派读取，反射构造中立模型。
/// <para>
/// 纪律：本件零领域词汇；<b>半规则层</b>（<c>step</c> 扁平四形态、<c>valueSource</c> 的多形态归一、<c>expr</c> /
/// <c>foreach</c> 等未登记子层）与 <see cref="MemberKind.Opaque"/>、派生位（无键成员）交给
/// <see cref="MemberHook"/> / <see cref="LayerHook"/>（命名钩子），不在此件内写死。
/// </para>
/// </summary>
internal static class StandardReaderEngine
{
    /// <summary>“未处理”哨兵：钩子返回它表示交回引擎按形态默认（区别于“已处理且结果为 null”）。</summary>
    public static readonly object Unhandled = new();

    /// <summary>层的注册表（子层解析用）。</summary>
    private static readonly IReadOnlyDictionary<string, LayerDescriptor> Registry =
        StandardDescriptors.All.ToDictionary(layer => layer.Layer, StringComparer.Ordinal);

    /// <summary>
    /// 成员钩子：由外部提供某层某成员的读取（可返回任意值，含 null；返回 <see cref="Unhandled"/> = 未处理，引擎按形态默认）。
    /// <paramref name="found"/> = 该成员是否命中声明键；<paramref name="element"/> = 命中键的值（未命中时为 default）；
    /// <paramref name="container"/> = 承载对象（供钩子读取兄弟键，如 <c>kind</c>）；
    /// <paramref name="context"/> = 成员键路径（如 <c>ports.A.B.impl</c>）。
    /// </summary>
    public delegate object? MemberHook(
        string layer, string member, string? jsonKey, bool found, JsonElement element, JsonElement container, string context);

    /// <summary>层钩子：整层接管（如 <c>step</c> 四形态判别）；返回 <see cref="Unhandled"/> = 未处理，引擎按描述符结构读取。
    /// <paramref name="context"/> = 层键路径（如 <c>ports.A.B.impl.script</c>；供诊断文案与半规则层委托）。</summary>
    public delegate object? LayerHook(string layer, JsonElement obj, string context);

    private static MemberHook? _memberHook;
    private static LayerHook? _layerHook;

    /// <summary>装配成员钩子（组合根注入；未装配 = 半规则成员不可读）。</summary>
    public static void Wire(MemberHook hook) => _memberHook = hook;

    /// <summary>装配层钩子（组合根注入；未装配 = 半规则层不可读）。</summary>
    public static void WireLayer(LayerHook hook) => _layerHook = hook;

    /// <summary>按层读取一个 JSON 对象为模型实例（可经层钩子整层接管；上下文 = 层名）。</summary>
    public static object? ReadLayer(LayerDescriptor layer, JsonElement obj)
        => ReadLayer(layer, obj, layer.Layer);

    /// <summary>
    /// 按层读取一个 JSON 对象为模型实例（<paramref name="context"/> = 键路径，供键闭包诊断与半规则层委托）。
    /// 顺序：层钩子（整层接管）→ 键闭包（未知键 fail-fast，取值域 = 描述符键集）→ 反射构造。
    /// </summary>
    public static object? ReadLayer(LayerDescriptor layer, JsonElement obj, string context)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException($"层 '{layer.Layer}' 须为对象");

        if (_layerHook is not null && TryHook(_layerHook(layer.Layer, obj, context), out var takeover))
            return takeover;

        // 键闭包：取值域 = 描述符键集（取代手抄 KeysOf）；收集 / 抛出体仍是 StandardJson.EnsureKnownKeys（唯一实现）。
        StandardJson.EnsureKnownKeys(context, obj, layer.Keys);

        var ctor = layer.Model.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var member = layer.Members.FirstOrDefault(m => string.Equals(m.Member, parameter.Name, StringComparison.Ordinal));
            args[i] = member is null ? DefaultOf(parameter) : ReadMember(layer, member, parameter, obj, context);
        }

        return ctor.Invoke(args);
    }

    private static object? ReadMember(
        LayerDescriptor layer, MemberDescriptor member, ParameterInfo parameter, JsonElement obj, string context)
    {
        var found = TryKey(member, obj, out var key, out var element);

        // 钩子先于“键缺失即默认”判定：无键 / 派生位成员（如 HasValue）亦须可接管。
        if (_memberHook is not null
            && TryHook(_memberHook(layer.Layer, member.Member, found ? key : member.JsonKey, found, element, obj, context),
                out var handled))
            return handled;

        if (!found)
        {
            if (member.Required)
                throw new StandardViolationException(
                    $"[port:standard] {context} 缺少必填键 '{member.JsonKey ?? member.Member}'（{member.Member}）");

            return member.EmptyOnAbsent ? EmptyOf(member) : DefaultOf(parameter);
        }

        var childContext = $"{context}.{key ?? member.Member}";
        return member.Kind switch
        {
            MemberKind.Scalar or MemberKind.Enum => ConvertScalar(element, parameter.ParameterType),
            MemberKind.Payload => element.Clone(),
            MemberKind.Child => ReadChild(member, element, childContext),
            MemberKind.Children => ReadChildren(member, element, childContext),
            MemberKind.KeyedMap => ReadKeyedMap(member, element, childContext),
            MemberKind.Opaque => throw new StandardViolationException(
                $"层 '{layer.Layer}' 成员 '{member.Member}'（Opaque）无处理器（须装配钩子）"),
            _ => DefaultOf(parameter),
        };
    }

    /// <summary>钩子结果判定：仅 <see cref="Unhandled"/> 表示未处理；返回 null 视为“已处理且结果为 null”。</summary>
    private static bool TryHook(object? result, out object? value)
    {
        value = result;
        return !ReferenceEquals(result, Unhandled);
    }

    /// <summary>子节点读取（<see cref="MemberKind.Child"/>）：登记层走结构递归；未登记层（<c>expr</c> / <c>foreach</c>）经层钩子。</summary>
    private static object? ReadChild(MemberDescriptor member, JsonElement element, string context)
    {
        var name = member.ChildLayer
            ?? throw new StandardViolationException($"成员 '{member.Member}' 声明为 Child 但缺 ChildLayer");

        if (Registry.TryGetValue(name, out var layer))
            return ReadLayer(layer, element, context);

        if (_layerHook is not null && TryHook(_layerHook(name, element, context), out var handled))
            return handled;

        throw new StandardViolationException(
            $"成员 '{member.Member}' 的子层 '{name}' 未登记（半规则层须经钩子）");
    }

    /// <summary>键缺失时按形态归一无（<see cref="MemberDescriptor.EmptyOnAbsent"/>）。</summary>
    private static object? EmptyOf(MemberDescriptor member)
    {
        if (member.ChildLayer is not { } name || !Registry.TryGetValue(name, out var layer))
            return null;

        return member.Kind switch
        {
            MemberKind.Children => Activator.CreateInstance(typeof(List<>).MakeGenericType(layer.Model)),
            MemberKind.KeyedMap => Activator.CreateInstance(
                typeof(Dictionary<,>).MakeGenericType(typeof(string), layer.Model)),
            _ => null,
        };
    }

    private static bool TryKey(MemberDescriptor member, JsonElement obj, out string key, out JsonElement element)
    {
        foreach (var candidate in member.DeclaredKeys)
        {
            if (obj.TryGetProperty(candidate, out element))
            {
                key = candidate;
                return true;
            }
        }

        key = member.JsonKey ?? member.Member;
        element = default;
        return false;
    }

    private static LayerDescriptor ChildLayer(MemberDescriptor member)
        => member.ChildLayer is { } name && Registry.TryGetValue(name, out var layer)
            ? layer
            : throw new StandardViolationException(
                $"成员 '{member.Member}' 的子层 '{member.ChildLayer}' 未登记（半规则层须经钩子）");

    private static object? ReadChildren(MemberDescriptor member, JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Array) return null;

        var layer = ChildLayer(member);
        var list = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(layer.Model))!;
        var index = 0;
        foreach (var item in element.EnumerateArray()) list.Add(ReadLayer(layer, item, $"{context}[{index++}]"));
        return list;
    }

    private static object? ReadKeyedMap(MemberDescriptor member, JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        LayerDescriptor? childLayer = member.ChildLayer is { } name && Registry.TryGetValue(name, out var found)
            ? found
            : null;
        var valueType = childLayer?.Model ?? typeof(object);
        var dict = (System.Collections.IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;

        foreach (var property in element.EnumerateObject())
            dict[property.Name] = childLayer is not null
                ? ReadLayer(childLayer, property.Value, $"{context}.{property.Name}")
                : property.Value.Clone();

        return dict;
    }

    /// <summary>标量转换（按目标类型；字符串 / 布尔 / 整数 / 字符串序列）。</summary>
    private static object? ConvertScalar(JsonElement element, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (underlying == typeof(bool)) return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : (bool?)null;
        if (underlying == typeof(int)) return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : (int?)null;
        if (underlying == typeof(IReadOnlyList<string>))
        {
            if (element.ValueKind != JsonValueKind.Array) return null;
            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToList();
        }

        return DefaultOfType(type);
    }

    private static object? DefaultOf(ParameterInfo parameter)
        => parameter.HasDefaultValue ? parameter.DefaultValue : DefaultOfType(parameter.ParameterType);

    private static object? DefaultOfType(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
}
