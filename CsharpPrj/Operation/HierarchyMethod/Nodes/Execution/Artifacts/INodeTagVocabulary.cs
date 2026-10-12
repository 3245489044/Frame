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
        Marks = new Dictionary<string, int>(System.StringComparer.Ordinal)
        {
            [NodeTags.Numbered] = (int)NodeHistoryMark.Numbered,
            [NodeTags.Terminal] = (int)NodeHistoryMark.Terminal,
            [NodeTags.Port] = (int)NodeHistoryMark.Port,
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> Marks { get; }
}
