namespace Usharp.Semantics;

/// <summary>
/// 源位置值对象（文件 + 行）：诊断/重复判定/失败报告的**统一归因载体**。
/// <para>禁止用「声明 id 当文件名」「文件第 0 行」伪装来源；无法定位时用 <see cref="Unknown"/> 明示。</para>
/// </summary>
/// <param name="File">源文件（可为 <c>(memory)</c>）。</param>
/// <param name="Line">行号（1 起；0 = 无行信息）。</param>
public readonly record struct UsharpSourceRef(string File, int Line)
{
    /// <summary>未知来源（不伪装成某个文件）。</summary>
    public static UsharpSourceRef Unknown => new("(unknown)", 0);

    /// <summary>构造来源（空白文件 ⇒ <c>(unknown)</c>，行号 &lt; 0 归零）。</summary>
    /// <param name="file">源文件。</param>
    /// <param name="line">行号。</param>
    public static UsharpSourceRef At(string? file, int line)
        => new(string.IsNullOrWhiteSpace(file) ? "(unknown)" : file, line < 0 ? 0 : line);

    /// <summary>是否可定位（有文件名）。</summary>
    public bool IsLocated => !string.IsNullOrWhiteSpace(File) && File != "(unknown)";

    public override string ToString() => $"{File}:{Line}";
}
