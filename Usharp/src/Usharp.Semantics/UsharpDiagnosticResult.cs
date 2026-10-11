namespace Usharp.Semantics;

/// <summary>
/// 「领域载荷 + 诊断」产物基记录（**统一抽象**；命名带 <c>Usharp</c> 前缀以避与
/// <c>UniversalWorkflowFramework.Core.Diagnostics.DiagnosticResult</c> 的跨命名空间歧义）：error 判据单源 = <see cref="UsharpDiagnostics"/>，
/// 渲染单源 = <see cref="UsharpDiagnosticRenderer"/>。
/// <para>
/// 各发射 / 装配产物（发射层 8 产物 + 矩阵装配产物）只需声明领域载荷与自身 <c>Diagnostics</c> 位置参数，
/// 诊断面样板（<c>HasErrors</c> / <c>Report()</c>）在此单点，禁各处再复制。
/// </para>
/// </summary>
public abstract record UsharpDiagnosticResult
{
    /// <summary>诊断集（各产物以其领域载荷并列的 <c>Diagnostics</c> 位置参数暴露同一集）。</summary>
    protected abstract IReadOnlyList<UsharpDiagnostic> DiagnosticList { get; }

    /// <summary>是否含 error 诊断（判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    public bool HasErrors => UsharpDiagnostics.HasError(DiagnosticList);

    /// <summary>空诊断集占位措辞（缺省 = 渲染器通用占位；各面可覆写保留既有措辞）。</summary>
    protected virtual string EmptyReport => UsharpDiagnosticRenderer.Empty;

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(DiagnosticList, EmptyReport);
}
