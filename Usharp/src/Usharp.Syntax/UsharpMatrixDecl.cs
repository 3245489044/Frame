using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>矩阵的一行：输出坐标 = 线性表达式（引用取坐标名；带 `'` 后缀视为 next-state）。</summary>
public sealed record UsharpMatrixRow(string OutputKey, string Expression, IReadOnlyList<string> InputRefs)
{
    /// <summary>从缩进行 `outKey = expr` 解析（首个 `=` 切分；输出键剥离 next-state 后缀 `'`）。</summary>
    public static UsharpMatrixRow? Parse(string line)
    {
        var eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var output = line[..eq].Trim().TrimEnd('\'', '′').Trim();
        var expr = line[(eq + 1)..].Trim();
        if (output.Length == 0 || expr.Length == 0) return null;

        // 切词权威 = UsharpIdentifier（与 [Space] 坐标行、线性表达式词法共用同一字符集）
        var refs = new List<string>();
        foreach (var name in UsharpIdentifier.Scan(expr))
            if (!refs.Contains(name, StringComparer.Ordinal)) refs.Add(name);
        return new UsharpMatrixRow(output, expr, refs);
    }
}

/// <summary>[Matrix] Name: over=&lt;SpaceId&gt; 的语法视图（空间上的一次变换：行=输出坐标，cell=权重/映射）。</summary>
public sealed record UsharpMatrixDecl(
    string MatrixId,
    string? OverSpaceId,
    IReadOnlyList<UsharpMatrixRow> Rows,
    string SourceFile,
    int LineNumber)
{
    /// <summary>
    /// 层处理器绑定（<c>process=&lt;ProcessId&gt;</c>；空 = 未绑定）——本矩阵即该层的处理代数。
    /// **加法式 init 属性**（不改位置参数）；一层恰一个声明者（多者 → US1011）。
    /// </summary>
    public string? ProcessId { get; init; }

    public static UsharpMatrixDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var rows = new List<UsharpMatrixRow>();
        foreach (var raw in d.Continuation)
        {
            var row = UsharpMatrixRow.Parse(raw.Trim());
            if (row is not null) rows.Add(row);
        }
        var over = d.Field(UsharpLanguage.FieldOver);
        return new UsharpMatrixDecl(d.Name, string.IsNullOrWhiteSpace(over) ? null : over.Trim(),
            rows, d.SourceFile, d.LineNumber)
        {
            ProcessId = d.HasField(UsharpLanguage.FieldProcess)
                ? d.Field(UsharpLanguage.FieldProcess).Trim()
                : null,
        };
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
