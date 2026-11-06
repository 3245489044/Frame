namespace Usharp.Compute;

/// <summary>
/// 计算层诊断码。US8001–US8020 为静态诊断（只由计算包产出，不登记进 UsharpDiagnosticCatalog，
/// 以免改变纯声明路径的码表与计数）；US8401–US8410 为运行/版本诊断（不是编译诊断）。
/// 运行码不复用文档草案的 US81xx：US81xx 已被既有关系声明诊断占用。
/// </summary>
public static class ComputeCodes
{
    // ── 静态（编译期，Error）──
    public const string HeaderInvalid = "US8001";
    public const string BoundaryViolation = "US8002";
    public const string SyntaxInvalid = "US8003";
    public const string NameUndeclared = "US8004";
    public const string TypeMismatch = "US8005";
    public const string BindingDuplicate = "US8006";
    public const string WriteImmutable = "US8007";
    public const string ReadBeforeDefine = "US8008";
    public const string CalleeInvalid = "US8009";
    public const string BudgetMissing = "US8010";
    public const string BudgetInvalid = "US8011";
    public const string PurityInvalid = "US8012";
    public const string EffectUndeclared = "US8013";
    public const string ShareExceeded = "US8014";
    public const string LoopInvalid = "US8015";
    public const string RecursionBudgetMissing = "US8016";
    public const string RecursionNotEnabled = "US8017";
    public const string ProgramNotProducible = "US8020";

    // ── 运行 / 版本（不是编译诊断）──
    public const string BudgetExceeded = "US8401";
    public const string StackResourceExceeded = "US8402";
    public const string HeapResourceExceeded = "US8403";
    public const string EffectDenied = "US8404";
    public const string Cancelled = "US8405";
    public const string WatchdogTimeout = "US8406";
    public const string StateDiscarded = "US8407";
    public const string PortFailure = "US8408";
    public const string ArithmeticError = "US8409";
    public const string VersionRejected = "US8410";
}

/// <summary>计算包自己产出的诊断记录（不依赖中心码表，纯声明路径不感知）。</summary>
public sealed record ComputeDiagnostic(string Code, string Message, string File, int Line)
{
    public override string ToString() => $"{File}:{Line}: {Code} {Message}";
}
