using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models.Ir;
using Share.RoslynJson.Parsing;

namespace Share.RoslynJson.Configuration;

/// <summary>
/// 生成器配置读取骨架（通用）：JSON 文本 → IR 根对象 + 字符串数组提取。
/// 各领域的配置段 schema（工程映射 / 词表追加等）由调用方在 IR 上自行遍历，本类不定义任何领域键。
/// </summary>
public static class GeneratorConfigReader
{
    /// <summary>读取配置；失败返回 false 并给出人类可读错误（调用方转领域诊断码）。</summary>
    /// <param name="path">配置文件路径（仅用于错误描述）。</param>
    /// <param name="text">配置文本。</param>
    /// <param name="root">out：IR 根对象（失败时为 null）。</param>
    /// <param name="error">out：失败原因。</param>
    public static bool TryRead(string path, string text, out ConversionObject? root, out string? error)
    {
        root = null;
        error = null;

        ConversionNode parsed;
        try
        {
            parsed = JsonLiteralParser.Parse(text ?? string.Empty);
        }
        catch (JsonLiteralParseException ex)
        {
            error = "配置 JSON 非法（偏移 " + ex.Offset + "）：" + ex.Message;
            return false;
        }

        if (parsed is not ConversionObject obj)
        {
            error = "配置根须为对象";
            return false;
        }

        root = obj;
        return true;
    }

    /// <summary>把节点（须为字符串数组）逐项追加到目标列表；非数组即静默忽略。</summary>
    public static void ReadStrings(ConversionNode? node, List<string> target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (node is not ConversionArray array) return;

        foreach (var item in array.Items)
        {
            if (item is ConversionScalar { ScalarKind: ConversionScalarKind.String } scalar && scalar.Value is string value)
                target.Add(value);
        }
    }
}
