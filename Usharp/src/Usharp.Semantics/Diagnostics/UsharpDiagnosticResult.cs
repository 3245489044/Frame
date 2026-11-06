using FrameworkAbstractions.Diagnostics;

namespace Usharp.Semantics;

/// <summary>
/// 「领域载荷 + 诊断」产物基记录（**统一抽象**；命名带 <c>Usharp</c> 前缀以避与
/// <c>UniversalWorkflowFramework.Core.Diagnostics.DiagnosticResult</c> 的跨命名空间歧义）：error 判据单源 = <see cref="UsharpDiagnostics"/>，
/// 渲染单源 = <see cref="UsharpDiagnostic"/> 的 <c>ToString()</c>（<see cref="UsharpDiagnosticRenderer"/> 同源）。
/// <para>
/// 各发射 / 装配产物（发射层 8 产物 + 矩阵装配产物）只需声明领域载荷与自身 <c>Diagnostics</c> 位置参数，
/// 诊断面样板（<c>HasErrors</c> / <c>Report()</c>）在此单点，禁各处再复制。
/// </para>
/// <para>诊断报表机制单源 = <see cref="DiagnosticResultBase{TDiagnostic}"/>（Share）；本类只绑定 Usharp 判据与措辞。</para>
/// </summary>
public abstract record UsharpDiagnosticResult : DiagnosticResultBase<UsharpDiagnostic>
{
    /// <summary>诊断集（各产物以其领域载荷并列的 <c>Diagnostics</c> 位置参数暴露同一集）。</summary>
    protected abstract override IReadOnlyList<UsharpDiagnostic> DiagnosticList { get; }

    /// <summary>单条诊断是否为 error（判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    /// <param name="diagnostic">诊断。</param>
    protected override bool IsError(UsharpDiagnostic diagnostic) => UsharpDiagnostics.IsError(diagnostic);

    /// <summary>单条诊断渲染（单源 = <see cref="UsharpDiagnostic"/> 的 <c>ToString()</c>）。</summary>
    /// <param name="diagnostic">诊断。</param>
    protected override string Render(UsharpDiagnostic diagnostic) => diagnostic.ToString();

    /// <summary>空诊断集占位措辞（缺省 = 渲染器通用占位；各面可覆写保留既有措辞）。</summary>
    protected override string EmptyReport => UsharpDiagnosticRenderer.Empty;
}
