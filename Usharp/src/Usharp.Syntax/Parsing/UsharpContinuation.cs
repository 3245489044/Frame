using System;
using System.Collections.Generic;

namespace Usharp.Syntax;

/// <summary>
/// 续行 <c>Name = Value</c> 的**单一解析实现**：注释行跳过、空行跳过、无法解析的行原样落
/// <see cref="Parsed.UnparsedLines"/>（禁静默丢弃）。
/// <para>
/// 声明投影（<see cref="UsharpConfigDecl"/> / <see cref="UsharpConstDecl"/> 等）只负责头字段，
/// 续行一律经本类，避免逐类复制「注释跳过 + '=' 切分」逻辑。
/// </para>
/// </summary>
public static class UsharpContinuation
{
    /// <summary>解析结果（条目序 = 声明序）。</summary>
    /// <param name="Entries">可解析的键值条目。</param>
    /// <param name="UnparsedLines">无法解析的原始行（已 Trim）。</param>
    public sealed record Parsed(
        IReadOnlyList<UsharpConfigEntry> Entries,
        IReadOnlyList<string> UnparsedLines);

    /// <summary>解析续行集合。</summary>
    /// <param name="lines">原始续行（可为 null）。</param>
    public static Parsed Parse(IEnumerable<string>? lines)
    {
        var entries = new List<UsharpConfigEntry>();
        var unparsed = new List<string>();

        foreach (var raw in lines ?? Array.Empty<string>())
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            // 注释不是条目：`#` 起首整行跳过（含 `=` / `<=` 的注释不得被切成伪键值）。
            if (line[0] == '#') continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) { unparsed.Add(line); continue; }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length == 0) { unparsed.Add(line); continue; }

            entries.Add(new UsharpConfigEntry(key, value));
        }

        return new Parsed(entries, unparsed);
    }
}
