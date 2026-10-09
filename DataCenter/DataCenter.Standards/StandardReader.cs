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
    /// <summary>层的注册表（子层解析用）。</summary>
    private static readonly IReadOnlyDictionary<string, LayerDescriptor> Registry =
        StandardDescriptors.All.ToDictionary(layer => layer.Layer, StringComparer.Ordinal);

    /// <summary>按层读取一个 JSON 对象为模型实例（可经层读取器整层接管；上下文 = 层名）。</summary>
    public static object? ReadLayer(LayerDescriptor layer, JsonElement obj)
        => ReadLayer(layer, obj, layer.Layer);

    /// <summary>
    /// 按层读取一个 JSON 对象为模型实例（<paramref name="context"/> = 键路径，供键闭包诊断与半规则层委托）。
    /// 顺序：清单声明的层读取器（<see cref="LayerDescriptor.Reader"/>，整层接管）→ 键闭包（未知键 fail-fast，取值域 = 描述符键集）→ 反射构造。
    /// </summary>
    public static object? ReadLayer(LayerDescriptor layer, JsonElement obj, string context)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException($"层 '{layer.Layer}' 须为对象");

        if (StandardReaders.TryGetLayer(layer.Reader, out var reader))
            return reader(layer.Layer, obj, context);

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
        // 门控 / 定制读取先于“键缺失即默认”判定：无键 / 派生位成员（如 HasValue）亦须可接管。
        if (member.Gate is { } gate && !HasKind(obj, gate))
            return null;

        if (StandardReaders.TryGetMember(member.Reader, out var memberReader))
            return memberReader(context, obj);

        var found = TryKey(member, obj, out var key, out var element);

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

    /// <summary>承载对象的 <c>kind</c> 是否等于给定值（缺失 / 非字符串 → false）。</summary>
    private static bool HasKind(JsonElement container, string kind)
        => container.ValueKind == JsonValueKind.Object
           && container.TryGetProperty("kind", out var declared)
           && declared.ValueKind == JsonValueKind.String
           && string.Equals(declared.GetString(), kind, StringComparison.Ordinal);

    /// <summary>子节点读取（<see cref="MemberKind.Child"/>）：登记层走结构递归；未登记子层（<c>foreach</c>）经清单声明的层读取器。</summary>
    private static object? ReadChild(MemberDescriptor member, JsonElement element, string context)
    {
        var name = member.ChildLayer
            ?? throw new StandardViolationException($"成员 '{member.Member}' 声明为 Child 但缺 ChildLayer");

        if (Registry.TryGetValue(name, out var layer))
            return ReadLayer(layer, element, context);

        if (StandardReaders.TryGetLayer(name, out var reader))
            return reader(name, element, context);

        throw new StandardViolationException(
            $"成员 '{member.Member}' 的子层 '{name}' 未登记（半规则层须声明 Reader 或登记层）");
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
