namespace Usharp.Semantics;

/// <summary>
/// 诊断渲染**单一实现**：诊断集 → 单行报表（空集 → 占位符）。
/// <para>
/// 消费方：编译门面 / 矩阵装配 / 发射产物（emit / plan / schema / connect）的各 <c>Report()</c>
/// 与 CLI 逐行输出 —— 渲染格式在此单源，禁止各处再写 <c>string.Join</c> 报表（D6 收敛点）。
/// </para>
/// </summary>
public static class UsharpDiagnosticRenderer
{
    /// <summary>通用空报表占位（默认）。</summary>
    public const string Empty = "(no diagnostics)";

    /// <summary>渲染为单行报表（<paramref name="empty"/> = 空集占位符，各面可保留自己的措辞）。</summary>
    public static string Render(IReadOnlyList<UsharpDiagnostic> diagnostics, string empty = Empty)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Count == 0 ? empty : string.Join(Environment.NewLine, Lines(diagnostics));
    }

    /// <summary>逐条渲染（报表 = 逐行文本的换行拼接；与 <see cref="Render"/> 同源）。</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Select(d => d.ToString()).ToList();
    }
}
