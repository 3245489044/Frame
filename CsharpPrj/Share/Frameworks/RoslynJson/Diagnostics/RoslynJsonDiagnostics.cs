
using Microsoft.CodeAnalysis;

namespace Share.RoslynJson.Diagnostics;

/// <summary>
/// 框架级诊断描述符注册表（唯一来源）。领域生成器（如 AgentTest 用例生成器）在此之上补自己的 ID 段。
/// ID 段约定：RJ1xxx = 框架级；领域段由消费方自行分配。
/// </summary>
public static class RoslynJsonDiagnostics
{
    private const string Category = "RoslynJson";

    /// <summary>源文件解析失败（语法错误）。</summary>
    public static readonly DiagnosticDescriptor SourceParseError = new DiagnosticDescriptor(
        "RJ1001", "源文件解析失败", "解析失败：{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>不支持的语法（信息性，通常伴随兜底映射）。</summary>
    public static readonly DiagnosticDescriptor UnsupportedSyntax = new DiagnosticDescriptor(
        "RJ1002", "不支持的语法", "不支持的语法节点：{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>语法 → IR 映射失败。</summary>
    public static readonly DiagnosticDescriptor MappingFailed = new DiagnosticDescriptor(
        "RJ1003", "语法映射失败", "语法映射失败：{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>转换管线失败（含发射阶段）。</summary>
    public static readonly DiagnosticDescriptor ConversionFailed = new DiagnosticDescriptor(
        "RJ1004", "转换失败", "转换失败：{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>源生成器执行异常（兜底捕获，避免生成器崩溃导致编译不可诊断）。</summary>
    public static readonly DiagnosticDescriptor GeneratorFailure = new DiagnosticDescriptor(
        "RJ1005", "源生成器异常", "源生成器异常（{0}）：{1}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
