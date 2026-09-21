namespace Usharp.Semantics;

/// <summary>诊断严重度。Error = 编译失败（装载期闸A 不通过）；Warning = 可继续但不合格。</summary>
public enum UsharpSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Usharp 编译诊断：一律携带 <c>文件:行</c> 归因（禁止无源报错；对齐 ContractRegistry/ReferenceSchemaRegistry 的 ValidationIssue 模式）。
/// </summary>
public sealed record UsharpDiagnostic(
    UsharpSeverity Severity,
    string Code,
    string Message,
    string SourceFile,
    int LineNumber)
{
    public override string ToString()
        => $"[{SourceFile}:{LineNumber}] {(Severity == UsharpSeverity.Error ? "error" : "warn")} {Code}: {Message}";

    public static UsharpDiagnostic Error(string code, string message, string file, int line)
        => new(UsharpSeverity.Error, code, message, file, line);

    public static UsharpDiagnostic Warn(string code, string message, string file, int line)
        => new(UsharpSeverity.Warning, code, message, file, line);

    public static UsharpDiagnostic Info(string code, string message, string file, int line)
        => new(UsharpSeverity.Info, code, message, file, line);
}

/// <summary>
/// 诊断码（单一真源）。装载体：「SpaceDup/AxisDup」声明面；「SpaceUnknown/CoordUndeclared」= 闸A；
/// 「FlowStepUndeclared」= 原 C1 缺陷（声明被静默截断）的语言级判据；「CrDangling」= 原有 linter 检查 1。
/// </summary>
public static class UsharpCodes
{
    // 声明面
    public const string SpaceDup = "US0001";
    public const string AxisDup = "US0002";
    public const string DeclEmpty = "US0003";

    // 矩阵装配面（[Matrix] 行 → 既有矩阵实例；判据/治理读法见 U2b）
    public const string MatrixRowInvalid = "US0004";
    public const string AxisKindUnknown = "US0005";

    // 发射面（[Flow] → RoundSpec）
    public const string FlowNotDeclared = "US5001";
    public const string FlowStepUnmapped = "US5002";
    public const string SuccessCriterionUnknown = "US5003";

    // 闸A（引用必须落在已声明空间）
    public const string SpaceUnknown = "US1001";
    public const string CoordUndeclared = "US1002";
    public const string MatrixRowOutUndeclared = "US1003";
    public const string MatrixRowInputUndeclared = "US1004";
    public const string FlowStepUndeclared = "US1005";
    public const string SubFlowUnknown = "US1006";
    public const string RefShapeInvalid = "US1007";

    // 跨库引用（复用 DslIndex）
    public const string CrDangling = "US2001";
    public const string DeprecatedReferenced = "US2002";

    // 边（引用即边）报告面
    public const string EdgePeerUnknown = "US3001";

    // 图能力（经 IUsharpGraphBackend 端口）
    public const string CircularReference = "US4001";
    public const string EdgeTypeUnmapped = "US4002";
    public const string EdgeTypeUnknown = "US4003";
    public const string FlowIntegrityViolation = "US4004";

    // 能力未装配（Info，非错误）
    public const string GraphBackendUnavailable = "US9001";
}
