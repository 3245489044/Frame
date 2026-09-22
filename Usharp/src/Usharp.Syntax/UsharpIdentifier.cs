namespace Usharp.Syntax;

/// <summary>
/// 坐标标识符的**单一字符集与切词**（真源）：坐标名 = <c>[A-Za-z_][A-Za-z0-9_]*</c>。
/// <para>
/// 不含连字符：<c>-</c> 与线性表达式右式的减号运算符同形（放宽会使 <c>a-b</c> 一语两解），
/// 故声明面与引用面**一致拒绝**——两边共用本字符集，禁止各写一份（防「可声明不可引用」的假通过）。
/// </para>
/// <para>
/// 声明名（<c>d.Name</c>：<c>[Obs] agent-loop</c>、<c>process=progress-step</c> 等）**不**受本字符集约束：
/// 那是解析器给出的前缀后名称通道，非坐标。
/// </para>
/// </summary>
public static class UsharpIdentifier
{
    /// <summary>坐标标识符正则片段（首字符字母/下划线，其后字母/数字/下划线）——唯一字符集权威。</summary>
    public const string Pattern = "[A-Za-z_][A-Za-z0-9_]*";

    /// <summary>整串是否恰为一个坐标标识符。</summary>
    public static bool Matches(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!IsStart(text[0])) return false;
        for (var i = 1; i < text.Length; i++)
            if (!IsPart(text[i])) return false;
        return true;
    }

    /// <summary>
    /// 从 <paramref name="index"/> 处读一个坐标标识符（读不到返回 false 且不移动下标）。
    /// </summary>
    public static bool TryRead(string source, ref int index, out string name)
    {
        name = string.Empty;
        var start = index;
        if (start >= source.Length || !IsStart(source[start])) return false;

        var i = start + 1;
        while (i < source.Length && IsPart(source[i])) i++;

        name = source[start..i];
        index = i;
        return true;
    }

    /// <summary>按位置序扫描文本中的全部坐标标识符（重复保留；调用方自行去重）。</summary>
    public static IEnumerable<string> Scan(string text)
    {
        for (var i = 0; i < text.Length; )
        {
            if (TryRead(text, ref i, out var name))
            {
                yield return name;
                continue;
            }
            i++;
        }
    }

    private static bool IsStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
