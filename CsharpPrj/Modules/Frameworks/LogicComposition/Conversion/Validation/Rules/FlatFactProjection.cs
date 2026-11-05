using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 扁平事实投影（通用）：把「点分键 → 值」的扁平事实集投影为嵌套 <see cref="ConversionObject"/>，
/// 使判据表（数据面 <c>*.rules.json</c>）里的取值路径（<c>kill.lease.generation</c>）与运行期事实的承载形态一一对应。
/// <para>
/// 语义：值为 <c>null</c> 表示**缺省**（键仍落位，按「缺失」参与判据 —— 与「值为空串」区分）。
/// 键空白项忽略；同一前缀重复出现时复用已建对象（不覆盖既有子对象）。
/// </para>
/// </summary>
public static class FlatFactProjection
{
    /// <summary>由扁平事实集投影 IR 根。</summary>
    /// <param name="flat">事实键值（<c>点分键</c> → 值；值 null = 缺省）。</param>
    public static ConversionObject Build(IEnumerable<KeyValuePair<string, string?>> flat)
    {
        if (flat is null) throw new ArgumentNullException(nameof(flat));

        var root = new ConversionObject();
        foreach (var pair in flat)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            AddPath(root, pair.Key.Split('.'), pair.Value);
        }

        return root;
    }

    private static void AddPath(ConversionObject node, string[] segments, string? value)
    {
        var current = node;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current.TryGet(segments[i], out var child) && child is ConversionObject obj)
            {
                current = obj;
                continue;
            }

            var created = new ConversionObject();
            current.Add(segments[i], created);
            current = created;
        }

        current.Add(segments[segments.Length - 1], value);
    }
}
