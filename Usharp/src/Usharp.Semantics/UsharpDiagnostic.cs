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
/// 「FlowStepUndeclared」= 原 C1 缺陷（声明被静默截断）的语言级判据；「CrDangling」= 原有 linter 检查 1；
/// 「US6001–US6007」= 观测声明（<c>[Obs]</c>）闸A；「US9002」= 观测后端未装配可见化。
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

    /// <summary>层图非法（<c>contains=</c> 形状非法 / 父层未声明 / 层自根不可达 / 非自环层间环）。</summary>
    public const string SpaceHierarchyInvalid = "US1008";

    /// <summary>子空间坐标未在父空间声明（子集不成立 ⇒ 划分非法）。</summary>
    public const string SubSpaceAxisUndeclared = "US1009";

    /// <summary>矩阵行输出坐标属**祖先层**但不属自身层（越界写 / 父子双写）。</summary>
    public const string MatrixRowWriteOutOfSubSpace = "US1010";

    /// <summary>处理器绑定非单源（<c>process=</c> 为空 / 同一层重复声明处理器）。</summary>
    public const string ProcessorBindingNotSingle = "US1011";

    /// <summary>含自环（递归划分）的层未声明 <c>budget=</c> —— 不定深度风险前移可见化。</summary>
    public const string SelfRecursiveBudgetMissing = "US1012";

    /// <summary><c>[Ref] scope=</c> 指向未声明的作用域（白名单 = <c>[Obs] kind=scope</c> 声明面）。</summary>
    public const string RefScopeUndeclared = "US1014";

    /// <summary><c>connects=</c> 端点在本编译内不可解析（上游流号未声明 / 自连）。</summary>
    public const string ConnectEndpointUnknown = "US1015";

    /// <summary><c>connects=</c> 连接边类型为空（须形如 <c>&lt;上游流号&gt;:&lt;边类型&gt;</c>）。</summary>
    public const string ConnectEdgeTypeEmpty = "US1016";

    /// <summary>跨块写依赖未声明：块读了由非祖先块写的坐标，而无 <c>connects=</c> 声明。</summary>
    public const string CrossBlockWriteDependencyMissing = "US1017";

    /// <summary>处理编排字段取值非法（<c>order=</c> 未知值 / <c>budget=</c> 非两段正整数）——按缺省处理。</summary>
    public const string PlanFieldInvalid = "US1018";

    // 跨库引用（复用 DslIndex）
    public const string CrDangling = "US2001";
    public const string DeprecatedReferenced = "US2002";

    // 边（引用即边）报告面
    public const string EdgePeerUnknown = "US3001";

    // 观测声明面（[Obs]：第五语言单元；编译产出 BoundaryContract/EventContract）
    public const string ObsEntityScopeUndefined = "US6001";
    public const string ObsEmitUndeclared = "US6002";
    public const string ObsDeclDuplicate = "US6003";
    public const string ObsEnumFieldNotRequired = "US6004";
    public const string ObsEventUnconsumed = "US6005";
    public const string ObsFileDuplicate = "US6006";
    public const string ObsKindUnknown = "US6007";

    // 图能力（经 IUsharpGraphBackend 端口）
    public const string CircularReference = "US4001";
    public const string EdgeTypeUnmapped = "US4002";
    public const string EdgeTypeUnknown = "US4003";
    public const string FlowIntegrityViolation = "US4004";

    // 能力未装配（Info，非错误）
    public const string GraphBackendUnavailable = "US9001";
    public const string ObsBackendUnavailable = "US9002";
}
