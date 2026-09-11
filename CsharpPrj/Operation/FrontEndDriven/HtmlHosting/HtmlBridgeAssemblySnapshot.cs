#nullable enable

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// HTML 装配清单快照 —— 通用宿主门面各 HTML 相关槽位装配状态的只读视图（组合根快照）。
/// 宿主装配后经此断言是否齐全，不再靠「记得调每个 AttachXxx」；缺绑定即可定位。
/// </summary>
public sealed record HtmlBridgeAssemblySnapshot(
    bool HtmlUiAttached,
    bool TemplatesAttached,
    bool PageDeclarationAttached,
    bool HtmlViewerResolverAttached,
    bool HtmlContentSinkAttached,
    bool DiagnosticsSinkAttached,
    bool ConfigPathSet)
{
    /// <summary>核心装配项是否齐全（诊断 sink 为可选观测项，不纳入必需集合）。</summary>
    public bool AllRequiredAttached
        => HtmlUiAttached && TemplatesAttached && PageDeclarationAttached
           && HtmlViewerResolverAttached && HtmlContentSinkAttached && ConfigPathSet;
}
