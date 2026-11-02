namespace Modules.Node.Text;

/// <summary>
/// 文本截断**策略表**（具名策略 + 单源实现）：把散落的「截断长度 + 是否扁平化」收敛为一处，
/// 消费方按**名**取用（<see cref="Summary"/> / <see cref="TableCell"/> / <see cref="Column"/>），
/// 消除「同一截断知识多处硬编码」的分叉（门禁：<c>TextClipSingleSource</c>）。
/// </summary>
public static class TextClipPolicy
{
    /// <summary>摘要策略：200 字，换行/回车扁平化为空格（塑形侧）。</summary>
    public const int SummaryMaxChars = 200;

    /// <summary>表格单元格策略：160 字（Markdown 单元格适配）。</summary>
    public const int TableCellMaxChars = 160;

    /// <summary>列摘要策略：80 字（行摘要列）。</summary>
    public const int ColumnMaxChars = 80;

    /// <summary>具名策略「摘要」：扁平化 + 200 字截断（null/空原样返回，保持 null 语义）。</summary>
    public static string? Summary(string? text) => Clip(text, SummaryMaxChars, flatten: true, preserveNull: true);

    /// <summary>具名策略「表格单元格」：160 字截断（无扁平化；null → 空串）。</summary>
    public static string TableCell(string? text) => Clip(text, TableCellMaxChars, flatten: false, preserveNull: false) ?? string.Empty;

    /// <summary>具名策略「列」：80 字截断（无扁平化；null → 空串）。</summary>
    public static string Column(string? text) => Clip(text, ColumnMaxChars, flatten: false, preserveNull: false) ?? string.Empty;

    /// <summary>单源截断实现：<paramref name="flatten"/> 是否扁平化换行；超出上限追加省略号。</summary>
    public static string? Clip(string? text, int max, bool flatten, bool preserveNull = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return preserveNull ? text : string.Empty;
        }

        var s = flatten ? text!.Replace('\r', ' ').Replace('\n', ' ') : text!;
        if (s.Length <= max)
        {
            return s;
        }

        // 截断点不得落在代理对中间（否则输出半个 UTF-16 码元）。
        var cut = max > 0 && max < s.Length && char.IsHighSurrogate(s[max - 1]) && char.IsLowSurrogate(s[max])
            ? max - 1
            : max;
        return s[..cut] + "…";
    }
}
