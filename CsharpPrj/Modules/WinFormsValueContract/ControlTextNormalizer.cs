using System.Windows.Forms;

namespace WinFormsValueContract;

/// <summary>
/// 控件显示值规范化器 —— 「字符串值 → 物理控件显示文本」的唯一权威入口。
/// 收敛历史散落在值契约 / WinFormsControlValueWriter / WinFormsControlAdapter.SetText 三处的裸
/// <c>control.Text = value</c>，统一处理两个跨层缺陷根因（如「输入与运行」JSON 编辑器没换行）：
///   1. 行尾规整：WinForms 文本框仅可靠渲染 CRLF，LF-only / 混用行尾会呈现为「没有换行」；
///   2. 多行时序：写入内容含换行时，须在赋值前确保 Multiline 已启用，否则单行框挤压不显示换行。
/// 策略：控件是否多行由「写入值是否含换行」决定（单行输入框保持单行，写多行内容自动转多行），非无差别强制。
/// 消费方：值契约（BuiltInValueContracts.Text）、WinFormsControlValueWriter、WinFormsControlAdapter.SetText。
/// </summary>
public static class ControlTextNormalizer
{
    /// <summary>规整行尾为 CRLF（幂等；null/空原样返回）。WinForms 显示换行的必要前提。</summary>
    public static string? NormalizeForDisplay(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    /// <summary>写入前守卫：确保 TextBoxBase 已启用多行，避免内容含换行时单行挤压。</summary>
    private static void EnsureMultiline(object control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.Multiline = true;
                break;
            case RichTextBox richTextBox:
                richTextBox.Multiline = true;
                break;
            case MaskedTextBox maskedTextBox:
                maskedTextBox.Multiline = true;
                break;
        }
    }

    /// <summary>
    /// 唯一写入入口：规整 CRLF + 按需启用多行 + 赋值，返回写后显示值（与物理控件 .Text 一致）。
    /// 所有「字符串 → 控件文本」写入必须经此，禁止再裸 <c>control.Text = value</c>。
    /// </summary>
    public static string? WriteText(object control, string? value)
    {
        var display = NormalizeForDisplay(value) ?? string.Empty;
        if (display.Contains('\n'))
            EnsureMultiline(control);

        if (control is Control c)
            c.Text = display;

        return display;
    }
}
