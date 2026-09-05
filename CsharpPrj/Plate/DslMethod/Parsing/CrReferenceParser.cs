using System.Text.RegularExpressions;
using Plate.DslMethod.Model;

namespace Plate.DslMethod.Parsing;

/// <summary>
/// [CR] 交叉引用解析器。文法对齐 dsl.md §4.1/§5：
/// 规范形态 `[CR: lib:STYP.NAME(.SUB)(@INTENT)]`；兼容旧形态 `[CR: file.md:anchor]`（lib 含点、无 STYP）。
/// </summary>
public static class CrReferenceParser
{
    // lib=非冒号非 ] 序列（可含点，兼容 file.md）；名=字母数字下划线点连字符；意图=小写字母序列。
    private static readonly Regex CrRegex = new(
        @"\[CR:\s*([^:\]]+?):([A-Za-z0-9_.\-]+?)(?:@([a-z]+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>从文本中提取全部 [CR] 引用。</summary>
    public static List<DslReference> ParseAll(string text, int lineNumber)
    {
        var result = new List<DslReference>();
        if (string.IsNullOrEmpty(text)) return result;

        foreach (Match m in CrRegex.Matches(text))
        {
            var name = m.Groups[2].Value;
            string? type = null, sub = null;

            // 首个点拆 STYP 与主名（如 AX.StateSnapshot / Def.op.needsSync）
            var dot = name.IndexOf('.');
            if (dot > 0)
            {
                type = name[..dot];
                name = name[(dot + 1)..];
            }
            // 主名内再有点 → 子谓词（如 StateSnapshot.deterministic）
            var dot2 = name.IndexOf('.');
            if (dot2 > 0)
            {
                sub = name[(dot2 + 1)..];
                name = name[..dot2];
            }

            result.Add(new DslReference
            {
                Lib = m.Groups[1].Value,
                Type = type,
                Name = name,
                Sub = sub,
                Intent = m.Groups[3].Success ? m.Groups[3].Value : null,
                Raw = m.Value,
                LineNumber = lineNumber,
            });
        }
        return result;
    }
}
