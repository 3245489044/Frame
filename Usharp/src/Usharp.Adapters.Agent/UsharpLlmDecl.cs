using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[LLMDecl] Name: over=&lt;空间&gt;</c> 的语法视图（LLM 判定契约语言单元）。
/// <para>
/// 把「判定形状与契约」外置：<c>system_body=</c> / <c>user_body=</c>（**提示词正文文件引用**——
/// 正文不入声明，由相对本声明的 <c>.md</c> 文件承载，符合项目「正文外置、声明只放引用」规范）、
/// <c>input=</c>（输入字段集）、<c>output=</c>（<c>&lt;根&gt;|&lt;字段&gt;… -&gt; &lt;类型&gt;</c>）。
/// </para>
/// <para>
/// 只做**语法**投影：LLM 推理本身非确定、不进声明语言；执行仍由内核 LLM 网关异步完成
/// （对齐 README §10「不新增决策机制」）。契约 ↔ 装配对账由闸A（<c>US7301</c>–<c>US7304</c>）承担。
/// </para>
/// </summary>
public sealed record UsharpLlmDecl(
    string Name,
    string? OverSpace,
    string? SystemBody,
    string? UserBody,
    IReadOnlyList<string> InputFields,
    string? OutputRoot,
    string? OutputType,
    IReadOnlyList<string> OutputFields,
    double? Temp,
    int? MaxCandidates,
    string SourceFile,
    int LineNumber)
{
    /// <summary>是否承载输出契约（<c>output=</c> 非空）。</summary>
    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputRoot);

    /// <summary>投影单条 <c>[LLMDecl]</c>（无名 ⇒ null，由闸A 报出）。</summary>
    public static UsharpLlmDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        // 续行 <key> = <value>（键序无关；后写覆盖）——与 [Config]/[Rule] 同款口径。
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length > 0) entries[key] = value;
        }

        string? Read(string key)
        {
            if (entries.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return UsharpLanguage.NullIfEmpty(d.Field(key));
        }

        var (outputRoot, outputType, outputFields) = ParseOutput(Read(UsharpLanguage.LlmFieldOutput));

        return new UsharpLlmDecl(
            d.Name.Trim(),
            UsharpLanguage.NullIfEmpty(Read(UsharpLanguage.LlmFieldOver)),
            UsharpLanguage.NullIfEmpty(Read(UsharpLanguage.LlmFieldSystemBody)),
            UsharpLanguage.NullIfEmpty(Read(UsharpLanguage.LlmFieldUserBody)),
            SplitList(Read(UsharpLanguage.LlmFieldInput)),
            outputRoot,
            outputType,
            outputFields,
            double.TryParse(Read(UsharpLanguage.LlmFieldTemp), out var t) ? t : null,
            int.TryParse(Read(UsharpLanguage.LlmFieldMaxCandidates), out var m) ? m : null,
            d.SourceFile,
            d.LineNumber);
    }

    /// <summary>
    /// <c>output=</c> 解析：<c>&lt;根&gt;|&lt;字段&gt;… -&gt; &lt;类型&gt;</c>；
    /// 根/字段的引号与空格归一（<c>"items"</c> ⇒ <c>items</c>）。
    /// </summary>
    private static (string? Root, string? Type, IReadOnlyList<string> Fields) ParseOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null, Array.Empty<string>());

        var arrow = value.IndexOf("->", StringComparison.Ordinal);
        var left = arrow < 0 ? value : value[..arrow];
        var type = arrow < 0 ? null : value[(arrow + 2)..].Trim();

        var tokens = left.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TrimQuotes)
            .Where(s => s.Length > 0)
            .ToList();

        var root = tokens.Count > 0 ? tokens[0] : null;
        var fields = tokens.Count > 1 ? tokens.Skip(1).ToList() : new List<string>();
        return (root, UsharpLanguage.NullIfEmpty(type), fields);
    }

    private static string TrimQuotes(string s) => s.Trim().Trim('"', '\'');

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
