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
    public const string SpaceDup = UsharpDiagnosticCatalog.SpaceDup;
    public const string AxisDup = UsharpDiagnosticCatalog.AxisDup;
    public const string DeclEmpty = UsharpDiagnosticCatalog.DeclEmpty;

    /// <summary>同名 <c>[Matrix]</c> 重复声明（E-3）。</summary>
    public const string MatrixIdDuplicate = UsharpDiagnosticCatalog.MatrixIdDuplicate;

    /// <summary>同名 <c>[Flow]</c> 重复声明（E-3）。</summary>
    public const string FlowIdDuplicate = UsharpDiagnosticCatalog.FlowIdDuplicate;

    /// <summary>多个 <c>[KeySpace]</c> 声明（E-3）。</summary>
    public const string KeySpaceDuplicate = UsharpDiagnosticCatalog.KeySpaceDuplicate;

    /// <summary>声明装载/回填/投影执行失败（E-4/R-3）。</summary>
    public const string DeclarationLoadFailure = UsharpDiagnosticCatalog.DeclarationLoadFailure;

    /// <summary>控制流子流嵌套超过 <c>MaxCallDepth</c>（截断可见化，保留前 N 层）。</summary>
    public const string CallDepthTruncated = UsharpDiagnosticCatalog.CallDepthTruncated;

    /// <summary>控制流绑定段不可解析（无 <c>=</c> / 空键 / 空值）。</summary>
    public const string ControlFlowSegmentUnparsed = UsharpDiagnosticCatalog.ControlFlowSegmentUnparsed;

    /// <summary><c>[Space]</c> 的 <c>kind=</c> 取值非法（合法值：<c>keys</c>|<c>values</c>）——不静默按 values 装配。</summary>
    public const string SpaceKindUnknown = UsharpDiagnosticCatalog.SpaceKindUnknown;

    /// <summary><c>[Space]</c> 缩进块内出现无法解析的坐标行（禁止静默丢弃声明行）。</summary>
    public const string AxisLineUnparsed = UsharpDiagnosticCatalog.AxisLineUnparsed;

    /// <summary><c>[Command] channel=</c> 不含 <c>.</c>（无法派生空间坐标；禁止静默放行）。</summary>
    public const string CommandChannelMalformed = UsharpDiagnosticCatalog.CommandChannelMalformed;

    /// <summary><c>[Command]</c> 引用的空间未声明（禁止把整段校验跳过）。</summary>
    public const string CommandSpaceUndeclared = UsharpDiagnosticCatalog.CommandSpaceUndeclared;

    /// <summary><c>[Rule] enabled=</c> 非法布尔值（禁止静默变 null）。</summary>
    public const string ReadModelDuplicate = UsharpDiagnosticCatalog.ReadModelDuplicate;

    public const string ReadModelSourceMissing = UsharpDiagnosticCatalog.ReadModelSourceMissing;

    public const string ReadModelVersionUnknown = UsharpDiagnosticCatalog.ReadModelVersionUnknown;

    public const string ReadModelTtlInvalid = UsharpDiagnosticCatalog.ReadModelTtlInvalid;

    public const string ReadModelProjectorMissing = UsharpDiagnosticCatalog.ReadModelProjectorMissing;

    public const string ReadModelLineUnparsed = UsharpDiagnosticCatalog.ReadModelLineUnparsed;

    public const string ReadModelOpRefUnknown = UsharpDiagnosticCatalog.ReadModelOpRefUnknown;

    public const string ReadModelUnused = UsharpDiagnosticCatalog.ReadModelUnused;

    public const string SegmentFieldDuplicate = UsharpDiagnosticCatalog.SegmentFieldDuplicate;

    public const string SegmentTypeUnknown = UsharpDiagnosticCatalog.SegmentTypeUnknown;

    public const string ReadModelFieldsUndeclared = UsharpDiagnosticCatalog.ReadModelFieldsUndeclared;

    public const string ReadModelFieldsEmpty = UsharpDiagnosticCatalog.ReadModelFieldsEmpty;

    public const string ReadModelFieldNoField = UsharpDiagnosticCatalog.ReadModelFieldNoField;

    public const string SegmentFieldMissing = UsharpDiagnosticCatalog.SegmentFieldMissing;

    // 成员级声明面（[Const]）闸A
    public const string ConstTargetInvalid = UsharpDiagnosticCatalog.ConstTargetInvalid;
    public const string ConstKindInvalid = UsharpDiagnosticCatalog.ConstKindInvalid;
    public const string ConstModifierInvalid = UsharpDiagnosticCatalog.ConstModifierInvalid;
    public const string ConstMemberInvalid = UsharpDiagnosticCatalog.ConstMemberInvalid;
    public const string ConstMemberDuplicate = UsharpDiagnosticCatalog.ConstMemberDuplicate;
    public const string ConstTargetConflict = UsharpDiagnosticCatalog.ConstTargetConflict;
    public const string ConstValueExpressionInvalid = UsharpDiagnosticCatalog.ConstValueExpressionInvalid;
    public const string ConstLineUnparsed = UsharpDiagnosticCatalog.ConstLineUnparsed;

    // 产品图声明面（[Product] / [Face]）闸A
    public const string ProductDuplicate = UsharpDiagnosticCatalog.ProductDuplicate;
    public const string ProductFieldInvalid = UsharpDiagnosticCatalog.ProductFieldInvalid;
    public const string ProductLevelUndeclared = UsharpDiagnosticCatalog.ProductLevelUndeclared;
    public const string ProductLineUnparsed = UsharpDiagnosticCatalog.ProductLineUnparsed;
    public const string ProductAcceptsInvalid = UsharpDiagnosticCatalog.ProductAcceptsInvalid;

    // UI 声明面（[Record] view_* / region_* / loc_*）发射
    public const string UiDeclarationShapeInvalid = UsharpDiagnosticCatalog.UiDeclarationShapeInvalid;
    public const string UiDeclarationNotProducible = UsharpDiagnosticCatalog.UiDeclarationNotProducible;
    public const string FaceDuplicate = UsharpDiagnosticCatalog.FaceDuplicate;
    public const string FaceProductUndeclared = UsharpDiagnosticCatalog.FaceProductUndeclared;
    public const string FaceFieldNotSubset = UsharpDiagnosticCatalog.FaceFieldNotSubset;
    public const string FaceFieldsEmpty = UsharpDiagnosticCatalog.FaceFieldsEmpty;
    public const string ProductGraphNotProducible = UsharpDiagnosticCatalog.ProductGraphNotProducible;

    /// <summary><c>[Rule] enabled=</c> 非法布尔值（禁止静默变 null）。</summary>
    public const string RuleEnabledInvalid = UsharpDiagnosticCatalog.RuleEnabledInvalid;

    /// <summary><c>[Obs] enum=</c> 段非法（禁止静默丢弃声明）。</summary>
    public const string ObsEnumSegmentInvalid = UsharpDiagnosticCatalog.ObsEnumSegmentInvalid;

    /// <summary>落库正文行不可解析（禁止仍返回成功）。</summary>
    public const string DocumentBodyLineUnparsed = UsharpDiagnosticCatalog.DocumentBodyLineUnparsed;

    // 矩阵装配面（[Matrix] 行 → 既有矩阵实例；判据/治理读法见 U2b）
    public const string MatrixRowInvalid = UsharpDiagnosticCatalog.MatrixRowInvalid;
    public const string AxisKindUnknown = UsharpDiagnosticCatalog.AxisKindUnknown;

    // 发射面（[Flow] → RoundSpec）
    public const string FlowNotDeclared = UsharpDiagnosticCatalog.FlowNotDeclared;
    public const string FlowStepUnmapped = UsharpDiagnosticCatalog.FlowStepUnmapped;
    public const string SuccessCriterionUnknown = UsharpDiagnosticCatalog.SuccessCriterionUnknown;

    /// <summary>层次产出面域内判据：<c>contains=</c> 图形状非法 / 单根不成立 / 一层多处理器绑定 ⇒ **不产出**（不复读闸A 码）。</summary>
    public const string HierarchySchemaNotProducible = UsharpDiagnosticCatalog.HierarchySchemaNotProducible;

    /// <summary>链尾重复连接（一条链尾至多接一个后继）：首个连接生效，后续 connects= 被忽略（可见化，不静默覆盖）。</summary>
    public const string ChainTailAlreadyLinked = UsharpDiagnosticCatalog.ChainTailAlreadyLinked;

    /// <summary>位置反解表不可产出（<c>[Matrix] LocToPlacement</c> 缺失 / 行输出不在反解域 / 引用了非 loc 词表项）。</summary>
    public const string LocPlacementNotProducible = UsharpDiagnosticCatalog.LocPlacementNotProducible;

    /// <summary>归属边声明不可产出（无任何带 <c>edge=</c> 的 <c>[Ref]</c> / 坐标未在空间声明）。</summary>
    public const string RefEdgeNotProducible = UsharpDiagnosticCatalog.RefEdgeNotProducible;

    /// <summary>节点域定义面不可产出（未找到该域 id 的 <c>[Domain]</c> / 形态非法）。</summary>
    public const string DomainSpecNotProducible = UsharpDiagnosticCatalog.DomainSpecNotProducible;

    /// <summary>节点聚拢声明不可产出（未找到该域 id 的 <c>[Gather]</c> / 形态非法）。</summary>
    public const string GatherSpecNotProducible = UsharpDiagnosticCatalog.GatherSpecNotProducible;

    /// <summary>
    /// 计划（处理编排）相关诊断码名单 —— 判据侧（<c>ProcessPlanGateRule</c> 产出）与发射侧
    /// （<c>UsharpHierarchyProcessPlanEmitter</c> 投影筛选）**共同引用**，避免名单两处维护（漏改即静默丢诊断）。
    /// </summary>
    public static readonly IReadOnlyList<string> PlanRelated =
        new[] { SelfRecursiveBudgetMissing, PlanFieldInvalid };

    // 闸A（引用必须落在已声明空间）
    public const string SpaceUnknown = UsharpDiagnosticCatalog.SpaceUnknown;
    public const string CoordUndeclared = UsharpDiagnosticCatalog.CoordUndeclared;
    public const string MatrixRowOutUndeclared = UsharpDiagnosticCatalog.MatrixRowOutUndeclared;
    public const string MatrixRowInputUndeclared = UsharpDiagnosticCatalog.MatrixRowInputUndeclared;
    public const string FlowStepUndeclared = UsharpDiagnosticCatalog.FlowStepUndeclared;
    public const string SubFlowUnknown = UsharpDiagnosticCatalog.SubFlowUnknown;
    public const string RefShapeInvalid = UsharpDiagnosticCatalog.RefShapeInvalid;

    /// <summary>层图非法（<c>contains=</c> 形状非法 / 父层未声明 / 层自根不可达 / 非自环层间环）。</summary>
    public const string SpaceHierarchyInvalid = UsharpDiagnosticCatalog.SpaceHierarchyInvalid;

    /// <summary>子空间坐标未在父空间声明（子集不成立 ⇒ 划分非法）。</summary>
    public const string SubSpaceAxisUndeclared = UsharpDiagnosticCatalog.SubSpaceAxisUndeclared;

    /// <summary>矩阵行输出坐标属**祖先层**但不属自身层（越界写 / 父子双写）。</summary>
    public const string MatrixRowWriteOutOfSubSpace = UsharpDiagnosticCatalog.MatrixRowWriteOutOfSubSpace;

    /// <summary>处理器绑定非单源（<c>process=</c> 为空 / 同一层重复声明处理器）。</summary>
    public const string ProcessorBindingNotSingle = UsharpDiagnosticCatalog.ProcessorBindingNotSingle;

    /// <summary>含自环（递归划分）的层未声明 <c>budget=</c> —— 不定深度风险前移可见化。</summary>
    public const string SelfRecursiveBudgetMissing = UsharpDiagnosticCatalog.SelfRecursiveBudgetMissing;

    /// <summary><c>[Ref] scope=</c> 指向未声明的作用域（白名单 = <c>[Obs] kind=scope</c> 声明面）。</summary>
    public const string RefScopeUndeclared = UsharpDiagnosticCatalog.RefScopeUndeclared;

    /// <summary><c>connects=</c> 端点在本编译内不可解析（上游流号未声明 / 自连）。</summary>
    public const string ConnectEndpointUnknown = UsharpDiagnosticCatalog.ConnectEndpointUnknown;

    /// <summary><c>connects=</c> 连接边类型为空（须形如 <c>&lt;上游流号&gt;:&lt;边类型&gt;</c>）。</summary>
    public const string ConnectEdgeTypeEmpty = UsharpDiagnosticCatalog.ConnectEdgeTypeEmpty;

    /// <summary>跨块写依赖未声明：块读了由非祖先块写的坐标，而无 <c>connects=</c> 声明。</summary>
    public const string CrossBlockWriteDependencyMissing = UsharpDiagnosticCatalog.CrossBlockWriteDependencyMissing;

    /// <summary>处理编排字段取值非法（<c>order=</c> 未知值 / <c>budget=</c> 非两段正整数）——按缺省处理。</summary>
    public const string PlanFieldInvalid = UsharpDiagnosticCatalog.PlanFieldInvalid;

    /// <summary>控制流绑定段重复键（<c>in=</c>/<c>out=</c>/<c>routes=</c>）——同键多值静默覆盖须可见化（E4）。</summary>
    public const string BindingKeyDuplicate = UsharpDiagnosticCatalog.BindingKeyDuplicate;

    // 跨库引用（复用 DslIndex）
    public const string CrDangling = UsharpDiagnosticCatalog.CrDangling;
    public const string DeprecatedReferenced = UsharpDiagnosticCatalog.DeprecatedReferenced;

    // 边（引用即边）报告面
    public const string EdgePeerUnknown = UsharpDiagnosticCatalog.EdgePeerUnknown;

    // 观测声明面（[Obs]：第五语言单元；编译产出 BoundaryContract/EventContract）
    public const string ObsEntityScopeUndefined = UsharpDiagnosticCatalog.ObsEntityScopeUndefined;
    public const string ObsEmitUndeclared = UsharpDiagnosticCatalog.ObsEmitUndeclared;
    public const string ObsDeclDuplicate = UsharpDiagnosticCatalog.ObsDeclDuplicate;
    public const string ObsEnumFieldNotRequired = UsharpDiagnosticCatalog.ObsEnumFieldNotRequired;
    public const string ObsEventUnconsumed = UsharpDiagnosticCatalog.ObsEventUnconsumed;
    public const string ObsFileDuplicate = UsharpDiagnosticCatalog.ObsFileDuplicate;
    public const string ObsKindUnknown = UsharpDiagnosticCatalog.ObsKindUnknown;

    // 域定义面（[Domain]：节点域定义面；U1）
    /// <summary>身份缺失，或位置键模板占位符不被身份占位符覆盖（身份占位符 ⊉ 键模板占位符）。</summary>
    public const string DomainIdentityInvalid = UsharpDiagnosticCatalog.DomainIdentityInvalid;

    /// <summary>同域位置 id 重复（位置 id 须唯一）。</summary>
    public const string DomainPlacementIdDuplicate = UsharpDiagnosticCatalog.DomainPlacementIdDuplicate;

    /// <summary><c>selection=</c> 引用未在 <c>selections</c> 词表声明的选择名。</summary>
    public const string DomainSelectionUndeclared = UsharpDiagnosticCatalog.DomainSelectionUndeclared;

    /// <summary><c>retention=</c> 引用未在 <c>retentions</c> 词表声明的保留等级。</summary>
    public const string DomainRetentionUndeclared = UsharpDiagnosticCatalog.DomainRetentionUndeclared;

    /// <summary>域声明形态非法（空 <c>placements</c> / 续行不可解析 / 缺必填字段）。</summary>
    public const string DomainShapeInvalid = UsharpDiagnosticCatalog.DomainShapeInvalid;

    // 聚拢声明面（[Gather]：收集端口枚举；U2）
    /// <summary>缺 <c>kind-field</c>（判别字段为族/塑形定位的唯一依据）。</summary>
    public const string GatherKindFieldMissing = UsharpDiagnosticCatalog.GatherKindFieldMissing;

    /// <summary>族名重复（族声明序为聚拢组间序的唯一依据）。</summary>
    public const string GatherFamilyDuplicate = UsharpDiagnosticCatalog.GatherFamilyDuplicate;

    /// <summary>塑形行缺 <c>family=</c>（未声明族归属）。</summary>
    public const string GatherShapingFamilyMissing = UsharpDiagnosticCatalog.GatherShapingFamilyMissing;

    /// <summary><c>summary</c> 字段声明但为空列表（须为逗号列表且非空）。</summary>
    public const string GatherShapingSummaryEmpty = UsharpDiagnosticCatalog.GatherShapingSummaryEmpty;

    /// <summary>缺通配塑形 <c>*</c>（治「未声明 kind 被丢行」）。</summary>
    public const string GatherWildcardShapingMissing = UsharpDiagnosticCatalog.GatherWildcardShapingMissing;

    /// <summary>聚拢声明形态非法（续行不可解析 / 族声明缺 <c>=</c> / 塑形行缺 kind）。</summary>
    public const string GatherShapeInvalid = UsharpDiagnosticCatalog.GatherShapeInvalid;


    // 判据读法面（[Matrix] over=<kind=terms 空间>；E1/U2b）
    /// <summary>词元行的 <c>zone=</c> 缺失或非法（象限须为 I/E/O 单字母）。</summary>
    public const string TermZoneInvalid = UsharpDiagnosticCatalog.TermZoneInvalid;

    /// <summary>规则的 <c>when=</c> 引用未声明的词元（<c>&lt;轴&gt;.&lt;词元&gt;</c>）。</summary>
    public const string DecisionTermUndeclared = UsharpDiagnosticCatalog.DecisionTermUndeclared;

    /// <summary>同层 <c>priority</c> 冲突（面内须全序显式支配；缺失归 <c>US1105</c>）。</summary>
    public const string DecisionPriorityConflict = UsharpDiagnosticCatalog.DecisionPriorityConflict;

    /// <summary>层 <c>absorb=</c> 的值不在该层标签集内（吸收集悬空）。</summary>
    public const string DecisionAbsorbUndeclared = UsharpDiagnosticCatalog.DecisionAbsorbUndeclared;

    /// <summary>判据读法形态非法（层缺 <c>output=</c> / 规则缺 <c>label=</c> / 层号不可解析 / 读法混用 / 无层）。</summary>
    public const string DecisionShapeInvalid = UsharpDiagnosticCatalog.DecisionShapeInvalid;

    /// <summary><c>cross.&lt;名&gt;</c> 引用未声明的复合特征。</summary>
    public const string DecisionCrossUndeclared = UsharpDiagnosticCatalog.DecisionCrossUndeclared;

    // 图能力（经 IUsharpGraphBackend 端口）
    public const string CircularReference = UsharpDiagnosticCatalog.CircularReference;
    public const string EdgeTypeUnmapped = UsharpDiagnosticCatalog.EdgeTypeUnmapped;
    public const string EdgeTypeUnknown = UsharpDiagnosticCatalog.EdgeTypeUnknown;
    public const string FlowIntegrityViolation = UsharpDiagnosticCatalog.FlowIntegrityViolation;

    // 能力未装配（Info，非错误）
    public const string GraphBackendUnavailable = UsharpDiagnosticCatalog.GraphBackendUnavailable;
    public const string ObsBackendUnavailable = UsharpDiagnosticCatalog.ObsBackendUnavailable;

    // 数据层（[Table] / [Record]：数据即声明）
    /// <summary>表声明形态非法（缺 <c>container=</c>/<c>key=</c>，或列行不可解析）。</summary>
    public const string TableShapeInvalid = UsharpDiagnosticCatalog.TableShapeInvalid;

    /// <summary>表列类型/标志不在受控词内。</summary>
    public const string TableColumnInvalid = UsharpDiagnosticCatalog.TableColumnInvalid;

    /// <summary>表列名重复（DDL 列序须唯一）。</summary>
    public const string TableColumnDuplicate = UsharpDiagnosticCatalog.TableColumnDuplicate;

    /// <summary>记录声明形态非法（续行值不可解析，或引用未声明的表）。</summary>
    public const string RecordShapeInvalid = UsharpDiagnosticCatalog.RecordShapeInvalid;

    /// <summary>记录引用的表未在本编译声明面中定义。</summary>
    public const string RecordTableUndeclared = UsharpDiagnosticCatalog.RecordTableUndeclared;

    /// <summary><c>[Record] table=</c> 使用容器名而非表 id（兼容期告警）。</summary>
    public const string RecordTableUsesContainer = UsharpDiagnosticCatalog.RecordTableUsesContainer;

    // LLM 判定契约面（[LLMDecl]：判定形状与契约外置；L 系闸A —— 输出/输入契约 ↔ 装配对账）
    /// <summary><c>[LLMDecl] output=</c> 的字段未在 <c>over=</c> 空间的 O 区词元中声明（消手写 Parse 的结构漂移）。</summary>
    public const string LlmContractOutputUndeclared = UsharpDiagnosticCatalog.LlmContractOutputUndeclared;

    /// <summary><c>[LLMDecl] input=</c> 的字段未在 <c>over=</c> 空间的 I 区词元中声明（消手写 BuildPrompt 的结构漂移）。</summary>
    public const string LlmContractInputUndeclared = UsharpDiagnosticCatalog.LlmContractInputUndeclared;

    /// <summary><c>[LLMDecl]</c> 形态非法（缺 <c>system=</c> / <c>output=</c> / 缺 <c>over=</c> 或 <c>over=</c> 非词元空间）。</summary>
    public const string LlmDeclShapeInvalid = UsharpDiagnosticCatalog.LlmDeclShapeInvalid;

    /// <summary><c>[LLMDecl] over=</c> 引用的空间未在本编译声明面中定义。</summary>
    public const string LlmDeclSpaceUnknown = UsharpDiagnosticCatalog.LlmDeclSpaceUnknown;

    // 端口执行体归属面（[Node] fn=/skill=/llm= 三选一；消「声明被校验却无人执行」）
    /// <summary><c>[Node] skill=</c> 引用的技能未落在已声明 <c>[Skill]</c> 空间（引用即边：未声明即编译报错）。</summary>
    public const string PortSkillUndeclared = UsharpDiagnosticCatalog.PortSkillUndeclared;

    /// <summary><c>[Node]</c> 的执行体归属非「恰一」（<c>fn=</c>/<c>skill=</c>/<c>llm=</c> 互斥完备：0 个或 ≥2 个）。</summary>
    public const string PortExecutorNotExclusive = UsharpDiagnosticCatalog.PortExecutorNotExclusive;

    /// <summary><c>[Node] kind=</c> 取值不在已声明执行体类别空间内（越界即编译报错）。</summary>
    public const string PortKindUnknown = UsharpDiagnosticCatalog.PortKindUnknown;

    // ── [KeySpace]（US74xx；从原误占的 US71xx 迁出）─────────────────────
    public const string KeyFamilyDuplicate = UsharpDiagnosticCatalog.KeyFamilyDuplicate;
    public const string KeyFamilyTemplateMissing = UsharpDiagnosticCatalog.KeyFamilyTemplateMissing;
    public const string KeySurfaceFamilyUnknown = UsharpDiagnosticCatalog.KeySurfaceFamilyUnknown;
    public const string KeyPortFamilyUnknown = UsharpDiagnosticCatalog.KeyPortFamilyUnknown;
    public const string KeyPolicyDuplicate = UsharpDiagnosticCatalog.KeyPolicyDuplicate;
    public const string KeyLineUnparsed = UsharpDiagnosticCatalog.KeyLineUnparsed;
    public const string SingleFileNoKeySpace = UsharpDiagnosticCatalog.SingleFileNoKeySpace;

    // ── [Command]（US75xx；从原误占的 US70xx 迁出）─────────────────────
    public const string CommandDuplicate = UsharpDiagnosticCatalog.CommandDuplicate;
    public const string CommandChannelUnknown = UsharpDiagnosticCatalog.CommandChannelUnknown;
    public const string CommandOpIncomplete = UsharpDiagnosticCatalog.CommandOpIncomplete;
    public const string CommandKeyFamilyUnknown = UsharpDiagnosticCatalog.CommandKeyFamilyUnknown;
    public const string CommandKeySurfaceUnknown = UsharpDiagnosticCatalog.CommandKeySurfaceUnknown;
    public const string CommandKeyChainFieldUnknown = UsharpDiagnosticCatalog.CommandKeyChainFieldUnknown;
    public const string CommandPortUnknown = UsharpDiagnosticCatalog.CommandPortUnknown;
    public const string CommandOpRefUnknown = UsharpDiagnosticCatalog.CommandOpRefUnknown;
    public const string CommandDefaultUnknown = UsharpDiagnosticCatalog.CommandDefaultUnknown;
    public const string CommandLineUnparsed = UsharpDiagnosticCatalog.CommandLineUnparsed;

    // ── Agent 适配面（US76xx；从原误占的 US7001–US7005 迁出）───────────
    public const string SkillBackendUnknown = UsharpDiagnosticCatalog.SkillBackendUnknown;
    public const string SkillBandUndeclared = UsharpDiagnosticCatalog.SkillBandUndeclared;
    public const string SkillBaseBandMissing = UsharpDiagnosticCatalog.SkillBaseBandMissing;
    public const string PortFnUnknown = UsharpDiagnosticCatalog.PortFnUnknown;
    public const string ToolTrackUnknown = UsharpDiagnosticCatalog.ToolTrackUnknown;
    public const string BehaviorActionUnknown = UsharpDiagnosticCatalog.BehaviorActionUnknown;

    // ── 关系声明面（US81xx）：[Relation] / [Invariant] / [Derive] ─────
    public const string RelationDuplicate = UsharpDiagnosticCatalog.RelationDuplicate;
    public const string RelationEdgeUndeclared = UsharpDiagnosticCatalog.RelationEdgeUndeclared;
    public const string RelationEndpointMismatch = UsharpDiagnosticCatalog.RelationEndpointMismatch;
    public const string RelationFlowUnknown = UsharpDiagnosticCatalog.RelationFlowUnknown;
    public const string RelationNamespaceUnknown = UsharpDiagnosticCatalog.RelationNamespaceUnknown;
    public const string RelationFieldInvalid = UsharpDiagnosticCatalog.RelationFieldInvalid;
    public const string RelationMountMissing = UsharpDiagnosticCatalog.RelationMountMissing;
    public const string RelationEmitUndeclared = UsharpDiagnosticCatalog.RelationEmitUndeclared;
    public const string InvariantDuplicate = UsharpDiagnosticCatalog.InvariantDuplicate;
    public const string InvariantEdgeUndeclared = UsharpDiagnosticCatalog.InvariantEdgeUndeclared;
    public const string InvariantKindUnknown = UsharpDiagnosticCatalog.InvariantKindUnknown;
    public const string InvariantOnViolationInvalid = UsharpDiagnosticCatalog.InvariantOnViolationInvalid;
    public const string InvariantScopeUndeclared = UsharpDiagnosticCatalog.InvariantScopeUndeclared;
    public const string InvariantDuplicateShape = UsharpDiagnosticCatalog.InvariantDuplicateShape;
    public const string InvariantGuardInvalid = UsharpDiagnosticCatalog.InvariantGuardInvalid;
    public const string DeriveDuplicate = UsharpDiagnosticCatalog.DeriveDuplicate;
    public const string DeriveEndpointUndeclared = UsharpDiagnosticCatalog.DeriveEndpointUndeclared;
    public const string DeriveEdgeUndeclared = UsharpDiagnosticCatalog.DeriveEdgeUndeclared;
    public const string DeriveViaBroken = UsharpDiagnosticCatalog.DeriveViaBroken;
    public const string DeriveFieldInvalid = UsharpDiagnosticCatalog.DeriveFieldInvalid;
    public const string DeriveEdgeDeprecated = UsharpDiagnosticCatalog.DeriveEdgeDeprecated;
    public const string DeriveDuplicateShape = UsharpDiagnosticCatalog.DeriveDuplicateShape;
    public const string RelationPlanNotProducible = UsharpDiagnosticCatalog.RelationPlanNotProducible;

    // 挂载绑定（[Mount]）闸A
    public const string MountDuplicate = UsharpDiagnosticCatalog.MountDuplicate;
    public const string MountRelationUndeclared = UsharpDiagnosticCatalog.MountRelationUndeclared;
    public const string MountTriggerUnknown = UsharpDiagnosticCatalog.MountTriggerUnknown;
    public const string MountFieldInvalid = UsharpDiagnosticCatalog.MountFieldInvalid;
    public const string MountRelationUnbound = UsharpDiagnosticCatalog.MountRelationUnbound;
}
