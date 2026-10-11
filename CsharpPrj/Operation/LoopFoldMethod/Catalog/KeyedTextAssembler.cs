using System;
using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Catalog;

/// <summary>一条装配消息（键 + 阶段 + 文本 + 轮次；通用；域无关）。</summary>
/// <param name="Key">条目键。</param>
/// <param name="Stage">阶段/类别（缺省回退为键）。</param>
/// <param name="Text">文本正文。</param>
/// <param name="Round">所属轮次（缺失 ⇒ <see cref="KeyedTextAssembler.UnattributedRound"/>）。</param>
public sealed record KeyedTextItem(string Key, string Stage, string Text, int Round);

/// <summary>
/// **有序文本装配**（通用；纯函数）—— 把「键 + 字段袋」序列装配为规范消息序列：
/// 输入序即输出序；空文本条目跳过（不抛）；阶段/轮次缺失按缺省回退。
/// </summary>
public static class KeyedTextAssembler
{
    /// <summary>未归属轮次的哨兵。</summary>
    public const int UnattributedRound = 0;

    /// <summary>装配（输入序即输出序；空文本条目跳过）。</summary>
    /// <param name="items">（键，字段袋）序列。</param>
    /// <param name="textField">文本字段名（缺省 <c>text</c>）。</param>
    /// <param name="stageField">阶段字段名（缺省 <c>kind</c>）。</param>
    /// <param name="roundField">轮次字段名（缺省 <c>round</c>）。</param>
    public static IReadOnlyList<KeyedTextItem> Assemble(
        IEnumerable<(string Key, IReadOnlyDictionary<string, string> Fields)> items,
        string textField = "text",
        string stageField = "kind",
        string roundField = "round")
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new List<KeyedTextItem>();
        foreach (var (key, fields) in items)
        {
            if (fields is null)
            {
                continue;
            }

            var text = fields.TryGetValue(textField, out var value) ? value : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var stage = fields.TryGetValue(stageField, out var kind) && !string.IsNullOrWhiteSpace(kind)
                ? kind
                : key;
            var round = fields.TryGetValue(roundField, out var roundText) && int.TryParse(roundText, out var roundValue)
                ? roundValue
                : UnattributedRound;

            result.Add(new KeyedTextItem(key, stage, text!, round));
        }

        return result;
    }
}
