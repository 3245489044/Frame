using System.Globalization;
using System.Text;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// **字节对拍门**（方案 R-1 的落地判据）：把「本工具渲染出的文本」与「磁盘上的既有文件」按
/// **UTF-8 无 BOM 原始字节**逐字节比较 —— 一致才允许写盘，差异即红并定位到行。
/// <para>
/// 由此，写侧不靠人眼核对，也不需要调用 PowerShell：口径（缩进 / 换行 / 尾 / 转义 / 数值 / 键序）
/// 任一处漂移都会当场被行级差异暴露。
/// </para>
/// </summary>
internal static class LedgerBytes
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>比较文本与文件字节：<c>null</c> = 逐字节一致；否则返回差异说明（行级定位；纯换行 / 字节层差异则报字节数）。</summary>
    internal static string? Drift(LedgerView view, string path, string expectedText)
    {
        if (!File.Exists(path)) return string.Format(view.Message("forgeFileMissing"), path);

        var expected = Utf8NoBom.GetBytes(expectedText);
        var actual = File.ReadAllBytes(path);
        if (expected.AsSpan().SequenceEqual(actual)) return null;

        return string.Format(view.Message("forgeDiffLine"), Describe(path, expectedText, expected, actual));
    }

    /// <summary>写盘（UTF-8 无 BOM，与既有产物一致；调用方须先过 <see cref="Drift"/>）。</summary>
    internal static void Write(string path, string text) => File.WriteAllBytes(path, Utf8NoBom.GetBytes(text));

    private static string Describe(string path, string expectedText, byte[] expected, byte[] actual)
    {
        var limit = Math.Min(expected.Length, actual.Length);
        var offset = 0;
        while (offset < limit && expected[offset] == actual[offset]) offset++;

        var expectedLines = SplitLines(expectedText);
        var actualLines = SplitLines(Utf8NoBom.GetString(actual));

        var line = 0;
        var shared = Math.Min(expectedLines.Length, actualLines.Length);
        while (line < shared && string.Equals(expectedLines[line], actualLines[line], StringComparison.Ordinal)) line++;

        var differing = line < shared;
        var where = differing
            ? string.Format(CultureInfo.InvariantCulture, "{0}（字节偏移 {1} / 行 {2}）", path, offset, line + 1)
            : string.Format(CultureInfo.InvariantCulture, "{0}（字节偏移 {1} / 全文 {2} 行内容一致）", path, offset, shared);

        return differing
            ? string.Format(
                CultureInfo.InvariantCulture, "{0}；期望「{1}」实得「{2}」",
                where, Clip(expectedLines[line]), Clip(actualLines[line]))
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}；差异在换行 / 字节层（期望 {1} 字节，实得 {2} 字节）",
                where, expected.Length, actual.Length);
    }

    /// <summary>按行切分（CRLF / CR / LF 一律归一，使差异定位不受换行口径影响 —— 换行漂移本身也是差异）。</summary>
    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string Clip(string line)
        => line.Length <= 120 ? line : line[..120] + "…";
}
