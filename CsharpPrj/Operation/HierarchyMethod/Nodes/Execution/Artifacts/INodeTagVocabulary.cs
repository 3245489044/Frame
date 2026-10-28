using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史标签词表端口（中立）—— 名称 ↔ 位值 的**真源接口**；宿主按声明面（<c>Config\nodes\*.ushar</c> marks）注入。
/// <para>框架不持宿主标记字面量；未注入 ⇒ <see cref="NodeTags"/> 用内置缺省词表（现行为）。</para>
/// </summary>
public interface INodeTagVocabulary
{
    /// <summary>标签名 → 位值（<c>None</c>=0 可缺省/含入；空集以 0 表示）。</summary>
    IReadOnlyDictionary<string, int> Marks { get; }
}

/// <summary>内置缺省词表（声明面词表未注入时的回退 = 现行为）。</summary>
public sealed class DefaultNodeTagVocabulary : INodeTagVocabulary
{
    /// <summary>缺省实例。</summary>
    public static readonly DefaultNodeTagVocabulary Instance = new();

    private DefaultNodeTagVocabulary()
    {
        Marks = BuildFromEnum();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> Marks { get; }

    /// <summary>由 <see cref="ContainerMark"/> 具名值**反射派生**（位值单源；<c>None=0</c> 不入映射 = 空集以 0 表示）。</summary>
    private static IReadOnlyDictionary<string, int> BuildFromEnum()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames(typeof(ContainerMark)))
        {
            var value = Convert.ToInt64(Enum.Parse(typeof(ContainerMark), name, ignoreCase: false));
            if (value == 0) continue;
            map[name] = (int)value;
        }

        return map;
    }
}
