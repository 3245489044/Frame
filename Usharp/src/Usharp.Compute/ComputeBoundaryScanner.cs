using Plate.DslMethod.Model;
using Usharp.Semantics;

namespace Usharp.Compute;

/// <summary>
/// R1 <c>usharp.compute.boundary</c>（<c>US8002</c>）的文本/声明扫描。
/// <para>
/// 只在「可能含计算」的文件上生效：声明级 <c>[Compute]</c>，或原文出现 <c>[Compute]</c> 标记
/// （含缩进写法——那种写法会被 <c>DslParser</c> 当续行吸收，必须显式报出而不是静默成为纯声明）。
/// 真正的纯声明文件（无标记、无声明）不进入本扫描，诊断零变化。
/// </para>
/// </summary>
public static class ComputeBoundaryScanner
{
    /// <summary>计算语句首词（只在 <c>[Compute]</c> 续行内合法）。</summary>
    public static readonly IReadOnlyList<string> StatementWords = new[]
    {
        "let", "var", "set", "if", "else", "end", "loop", "while", "return"
    };

    /// <summary>既有声明不得携带的保留计算字段名（字段值永不进入计算）。</summary>
    public static readonly IReadOnlyList<string> ReservedFields = new[]
    {
        "calc", "expr", "script", "body_expr"
    };

    /// <summary>含计算文件内须保持「纯数据」的声明头字段。</summary>
    public static readonly IReadOnlyList<string> HeaderDataFields = new[]
    {
        "judge", "selector", "inports", "outports"
    };

    private const string Fence = "```";

    /// <summary>闭包内是否存在「可能含计算」的文件（决定闸A 计算规则是否启用）。</summary>
    public static bool HasComputeMarker(IReadOnlyList<DslDocument> documents,
        IReadOnlyList<UsharpSourceFile> sources)
    {
        foreach (var document in documents)
            if (UsharpComputeClassifier.HasComputation(document)) return true;

        foreach (var source in sources)
        {
            var fence = false;
            foreach (var line in TextLines(source.Text ?? ""))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
                {
                    fence = !fence;
                    continue;
                }

                if (fence) continue;
                if (trimmed.StartsWith(UsharpComputeClassifier.ComputePrefix, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<ComputeDiagnostic> Scan(IReadOnlyList<DslDocument> documents,
        IReadOnlyList<UsharpSourceFile> sources)
    {
        var diagnostics = new List<ComputeDiagnostic>();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources) texts[source.Path] = source.Text ?? "";

        foreach (var document in documents)
        {
            var hasCompute = UsharpComputeClassifier.HasComputation(document);
            var text = texts.GetValueOrDefault(document.FilePath);

            if (text is not null)
                ScanText(document.FilePath, text, hasCompute, ComputeContinuationLines(document), diagnostics);

            if (hasCompute) ScanDeclarationHeads(document, diagnostics);
        }

        return diagnostics;
    }

    private static void ScanText(string file, string text, bool hasCompute,
        HashSet<int> computeLines, List<ComputeDiagnostic> diagnostics)
    {
        var lines = TextLines(text);
        var fence = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var raw = lines[index];
            var line = index + 1;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
            {
                fence = !fence;
                continue;
            }

            if (fence) continue;

            var indented = raw.Length != raw.TrimStart().Length;

            if (trimmed.StartsWith(UsharpComputeClassifier.ComputePrefix, StringComparison.Ordinal))
            {
                // 顶层 [Compute] 是声明（由解析器识别）；缩进写法会被吸收为续行，必须报出。
                if (indented)
                {
                    diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BoundaryViolation,
                        "缩进的 [Compute] 不是声明（会被当作上一行声明的续行吸收）；计算单元必须是顶层声明",
                        file, line));
                }

                continue;
            }

            if (!hasCompute) continue;

            var word = FirstWord(trimmed);
            if (!StatementWords.Contains(word, StringComparer.Ordinal)) continue;

            if (!indented)
            {
                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BoundaryViolation,
                    $"计算语句首词 '{word}' 出现在顶层：语句只能位于 [Compute] 声明的缩进续行内",
                    file, line));
                continue;
            }

            if (!computeLines.Contains(line))
            {
                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BoundaryViolation,
                    $"计算语句首词 '{word}' 位于非 [Compute] 声明的续行内：计算语句只能归属 [Compute] 单元",
                    file, line));
            }
        }
    }

    /// <summary>含计算文件的其它声明头：保留字段 + judge/selector/inports/outports 的纯数据文法。</summary>
    private static void ScanDeclarationHeads(DslDocument document, List<ComputeDiagnostic> diagnostics)
    {
        foreach (var declaration in document.Declarations)
        {
            if (UsharpComputeClassifier.IsComputeUnit(declaration)) continue;

            foreach (var field in ReservedFields)
            {
                if (!declaration.HasField(field)) continue;
                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BoundaryViolation,
                    $"{declaration.Prefix} {declaration.Name}: 不得声明保留计算字段 '{field}='"
                    + "（既有声明的头永远是数据；需要计算请新建 [Compute] 单元）",
                    declaration.SourceFile, declaration.LineNumber));
            }

            foreach (var field in HeaderDataFields)
            {
                if (!declaration.HasField(field)) continue;
                var value = declaration.Field(field);
                if (value.Length == 0) continue;

                var ok = field is "judge" or "selector"
                    ? Usharp.Syntax.UsharpIdentifier.Matches(value)
                    : IsPortList(value);
                if (ok) continue;

                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BoundaryViolation,
                    $"{declaration.Prefix} {declaration.Name}: {field}='{value}' 含非标识符记号"
                    + "（含计算的文件里，声明头字段只接受坐标/端口标识符文法，不接受表达式）",
                    declaration.SourceFile, declaration.LineNumber));
            }
        }
    }

    private static bool IsPortList(string value)
    {
        var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts)
            if (!Usharp.Syntax.UsharpIdentifier.Matches(part)) return false;
        return true;
    }

    /// <summary>各 <c>[Compute]</c> 声明的续行行号集（语句归属的声明范围）。</summary>
    private static HashSet<int> ComputeContinuationLines(DslDocument document)
    {
        var lines = new HashSet<int>();
        foreach (var declaration in document.Declarations)
        {
            if (!UsharpComputeClassifier.IsComputeUnit(declaration)) continue;
            for (var i = 1; i <= declaration.Continuation.Count; i++)
                lines.Add(declaration.LineNumber + i);
        }

        return lines;
    }

    private static List<string> TextLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();

    private static string FirstWord(string text)
    {
        var index = 0;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        return text[..index];
    }
}
