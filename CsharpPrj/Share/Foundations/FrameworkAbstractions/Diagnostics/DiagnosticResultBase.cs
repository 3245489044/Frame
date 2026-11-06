using System.Collections.Generic;

namespace FrameworkAbstractions.Diagnostics;

/// <summary>
/// 「领域载荷 + 诊断」产物基记录（通用件）：error 判据、渲染与空集占位在此单点，
/// 各产物只声明自身载荷与诊断集。
/// <para>派生方必须提供四项：诊断集位置、单条 error 判据、单条渲染、空集措辞（措辞属各领域契约，留在领域侧）。</para>
/// </summary>
public abstract record DiagnosticResultBase<TDiagnostic>
{
    /// <summary>诊断集（派生方以其领域载荷并列的位置参数暴露同一集）。</summary>
    protected abstract IReadOnlyList<TDiagnostic> DiagnosticList { get; }

    /// <summary>是否含 error 诊断（判据由派生方给定）。</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in DiagnosticList)
            {
                if (IsError(diagnostic)) return true;
            }

            return false;
        }
    }

    /// <summary>诊断单行报表；空集 ⇒ <see cref="EmptyReport"/>。</summary>
    public string Report()
    {
        if (DiagnosticList.Count == 0) return EmptyReport;

        var lines = new List<string>(DiagnosticList.Count);
        foreach (var diagnostic in DiagnosticList) lines.Add(Render(diagnostic));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>单条诊断是否 error。</summary>
    /// <param name="diagnostic">诊断。</param>
    protected abstract bool IsError(TDiagnostic diagnostic);

    /// <summary>单条诊断的单行渲染。</summary>
    /// <param name="diagnostic">诊断。</param>
    protected abstract string Render(TDiagnostic diagnostic);

    /// <summary>空诊断集占位措辞。</summary>
    protected abstract string EmptyReport { get; }
}
