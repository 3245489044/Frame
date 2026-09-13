namespace Share.LogicComposition.Conversion.Diagnostics;

/// <summary>
/// 框架级诊断码常量（**唯一来源**）：映射器 / 发射器 / 转换服务只引用本类，禁止再写字面量。
/// <para>
/// 码段约定：<c>RJ1xxx</c> = 框架级。领域生成器（如 AgentTest 用例生成器）另分配自己的段
/// （见 <c>SpecDiagnosticCodes</c>），并把本目录并入自己的诊断描述符目录。
/// </para>
/// <para>
/// <see cref="ToErrorClass"/> 给出码 → 错误类映射，元素域对齐 .clinerules 的
/// <c>Gate.ClassifyError</c>（parse_error / partial_output / execution_failure …），供宿主统一熔断归类。
/// </para>
/// </summary>
public static class RoslynJsonDiagnosticCodes
{
    /// <summary>RJ1001：源文件解析失败（语法错误）。</summary>
    public const string SourceParseError = "RJ1001";

    /// <summary>RJ1002：不支持的语法（信息性，通常伴随兜底映射）。</summary>
    public const string UnsupportedSyntax = "RJ1002";

    /// <summary>RJ1003：语法 → IR 映射失败。</summary>
    public const string MappingFailed = "RJ1003";

    /// <summary>RJ1004：转换管线失败（含发射阶段）。</summary>
    public const string ConversionFailed = "RJ1004";

    /// <summary>RJ1005：源生成器执行异常。</summary>
    public const string GeneratorFailure = "RJ1005";

    /// <summary>RJ1006：语法节点未注册映射规则（走兜底映射）。</summary>
    public const string MapUnmapped = "RJ1006";

    /// <summary>RJ1007：兜底映射深度超上限（已截断子节点）。</summary>
    public const string MapMaxDepth = "RJ1007";

    /// <summary>RJ1008：待发射节点为 null。</summary>
    public const string EmitNull = "RJ1008";

    /// <summary>RJ1009：发射类名非法（空）。</summary>
    public const string EmitClassName = "RJ1009";

    /// <summary>RJ1010：发射成员名非法（空）。</summary>
    public const string EmitMemberName = "RJ1010";

    /// <summary>RJ1011：C# 源码生成失败。</summary>
    public const string EmitGenerationFailed = "RJ1011";

    /// <summary>
    /// 码 → 错误类（对齐 <c>Gate.ClassifyError</c> 元素域）：
    /// 格式/来源不可用归 parse_error，部分可用归 partial_output，执行失败归 execution_failure。
    /// 未登记码回落 execution_failure（与诊断描述符目录的兜底语义一致）。
    /// </summary>
    /// <param name="code">稳定诊断码（null/未登记 → execution_failure）。</param>
    public static string ToErrorClass(string? code) => code switch
    {
        SourceParseError => "parse_error",
        MapMaxDepth => "parse_error",
        EmitNull => "parse_error",
        EmitClassName => "parse_error",
        EmitMemberName => "parse_error",
        UnsupportedSyntax => "partial_output",
        MapUnmapped => "partial_output",
        _ => "execution_failure",
    };
}
