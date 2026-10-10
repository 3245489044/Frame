using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>配置节的一条续行键值（<c>key = value</c>；值保留原文，不做类型推断）。</summary>
/// <param name="Key">键名。</param>
/// <param name="Value">值原文（已去空白）。</param>
public sealed record UsharpConfigEntry(string Key, string Value);

/// <summary>
/// <c>[Config] Name</c> 语法视图（配置面：让配置项由声明派生）。
/// <para>书写：叶 = <c>[Config] &lt;path&gt;: value=&lt;标量&gt;</c>；节 = <c>[Config] &lt;path&gt;</c> +
/// 续行 <c>&lt;key&gt; = &lt;value&gt;</c>（键序 = 声明序）。</para>
/// <para>本类只做**语法**投影：值语义（类型/枚举/必填）由装载侧裁决；续行中无法解析的行原文落入
/// <see cref="UnparsedLines"/>（禁止静默丢弃）。</para>
/// </summary>
public sealed record UsharpConfigDecl(
    string Path,
    string? Value,
    IReadOnlyList<UsharpConfigEntry> Entries,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>是否叶声明（含 <c>value=</c>）。</summary>
    public bool IsLeaf => Value is not null;

    /// <summary>投影单条 <c>[Config]</c>（无名 ⇒ null）。</summary>
    public static UsharpConfigDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var value = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldValue));

        var entries = new List<UsharpConfigEntry>();
        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) { unparsed.Add(line); continue; }

            var key = line[..eq].Trim();
            var entryValue = line[(eq + 1)..].Trim();
            if (key.Length == 0) { unparsed.Add(line); continue; }

            entries.Add(new UsharpConfigEntry(key, entryValue));
        }

        return new UsharpConfigDecl(d.Name, value, entries, unparsed, d.SourceFile, d.LineNumber);
    }
}
