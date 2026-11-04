using System;
using System.Collections.Generic;
using System.Linq;

namespace Usharp.Semantics;

/// <summary>一条诊断码的登记事实（码值 / 归属单元 / 严重度 / 语义 / 参数说明）。</summary>
public sealed record UsharpCodeSpec(
    string Code,
    string OwnerUnit,
    UsharpSeverity Severity,
    string Message,
    string Parameters);

/// <summary>
/// 诊断码唯一真源（替代 <c>UsharpCodes</c> / <c>UsharpKeyCodes</c> / 各 Agent 包 <c>Code*</c> 三张码表）。
/// <para>
/// 段位：<c>US0xxx</c> 通用声明面 · <c>US1xxx</c> 闸A · <c>US2xxx/US3xxx/US4xxx</c> 跨库引用/边/图 ·
/// <c>US5xxx</c> 发射面 · <c>US6xxx</c> <c>[Obs]</c> · <c>US7xxx</c> 语言单元闸A（70 Domain / 71 Gather /
/// 72 Table·Record / 73 LLMDecl / 74 KeySpace / 75 Command / 76 Agent 适配 / 78 端口执行体） · <c>US9xxx</c> 能力未装配。
/// </para>
/// <para>码值属对外契约：新增/改号须同步 README §5 与 CentralTest 断言；唯一性由 <see cref="AssertUnique"/> 与单测双保险。</para>
/// </summary>
public static class UsharpDiagnosticCatalog
{
    // ── US0xxx 通用声明面 ────────────────────────────────────────────────
    public const string SpaceDup = "US0001";
    public const string AxisDup = "US0002";
    public const string DeclEmpty = "US0003";
    public const string MatrixRowInvalid = "US0004";
    public const string AxisKindUnknown = "US0005";
    public const string SpaceKindUnknown = "US0006";
    public const string AxisLineUnparsed = "US0007";
    public const string CommandChannelMalformed = "US0008";
    public const string CommandSpaceUndeclared = "US0009";
    public const string RuleEnabledInvalid = "US0010";
    public const string ObsEnumSegmentInvalid = "US0011";
    public const string DocumentBodyLineUnparsed = "US0012";

    /// <summary>声明行前缀未在任何已装载领域包注册（未装载 ⇒ 不识别；禁静默丢声明）。</summary>
    public const string UnregisteredPrefix = "US0013";

    // ── US00xx 声明身份/装载面 ──────────────────────────────────────────
    /// <summary>同名 <c>[Matrix]</c> 重复声明（产物只取 winner，重复必须可见化）。</summary>
    public const string MatrixIdDuplicate = "US0014";

    /// <summary>同名 <c>[Flow]</c> 重复声明。</summary>
    public const string FlowIdDuplicate = "US0015";

    /// <summary>多个 <c>[KeySpace]</c> 声明（产物为单键空间模型；多声明须显式拒绝，不得静默只取首个）。</summary>
    public const string KeySpaceDuplicate = "US0016";

    /// <summary>声明装载/回填/投影执行异常（失败原因必须可见，不静默吞）。</summary>
    public const string DeclarationLoadFailure = "US0017";

    /// <summary>控制流子流嵌套超过 <c>MaxCallDepth</c>：截断可见化（保留前 N 层，不再静默返回空表）。</summary>
    public const string CallDepthTruncated = "US0018";

    /// <summary>控制流绑定段不可解析（<c>in=</c>/<c>out=</c>/<c>routes=</c> 中的无 <c>=</c> 段 / 空键 / 空值）。</summary>
    public const string ControlFlowSegmentUnparsed = "US0019";

    // ── US79xx 键身份语义层（[Entity]/[KeyLexicon]/[AnswerShape]/[WindowShape]/[KeyPolicy]/[KeyBridge]）──
    /// <summary>[Entity] 语义轴字段缺失/非法。</summary>
    public const string IdentityEntityInvalid = "US7901";

    /// <summary>语义实体在 family 表中的权威面不唯一（S1/J3）。</summary>
    public const string IdentityAuthorityNotSingle = "US7902";

    /// <summary>词表重叠（A4：ProcessKind/ContentKind/AnswerPart 互斥）。</summary>
    public const string IdentityLexiconOverlap = "US7903";

    /// <summary>形状契约缺必填项或 default 未声明（未命中处置不完整）。</summary>
    public const string IdentityShapeInvalid = "US7904";

    /// <summary>[KeyPolicy] 写侧策略字段缺失（唯一写者/基数/不可判处置）。</summary>
    public const string IdentityPolicyInvalid = "US7905";

    /// <summary>[KeyBridge] 跨域映射字段缺失或可逆性未声明。</summary>
    public const string IdentityBridgeInvalid = "US7906";

    /// <summary>语义实体尚无权威族（信息级：语义层已声明，语法族待接）。</summary>
    public const string IdentityAuthorityPending = "US7907";

    /// <summary>身份声明行含无法解析的字段段（坏段不得静默丢弃，R-9）。</summary>
    public const string IdentityLineUnparsed = "US7908";

    // ── US1xxx 闸A ──────────────────────────────────────────────────────
    public const string SpaceUnknown = "US1001";
    public const string CoordUndeclared = "US1002";
    public const string MatrixRowOutUndeclared = "US1003";
    public const string MatrixRowInputUndeclared = "US1004";
    public const string FlowStepUndeclared = "US1005";
    public const string SubFlowUnknown = "US1006";
    public const string RefShapeInvalid = "US1007";
    public const string SpaceHierarchyInvalid = "US1008";
    public const string SubSpaceAxisUndeclared = "US1009";
    public const string MatrixRowWriteOutOfSubSpace = "US1010";
    public const string ProcessorBindingNotSingle = "US1011";
    public const string SelfRecursiveBudgetMissing = "US1012";
    public const string RefScopeUndeclared = "US1014";
    public const string ConnectEndpointUnknown = "US1015";
    public const string ConnectEdgeTypeEmpty = "US1016";
    public const string CrossBlockWriteDependencyMissing = "US1017";
    public const string PlanFieldInvalid = "US1018";
    public const string BindingKeyDuplicate = "US1019";

    // ── US1xxx 判据读法面 ───────────────────────────────────────────────
    public const string TermZoneInvalid = "US1101";
    public const string DecisionTermUndeclared = "US1102";
    public const string DecisionPriorityConflict = "US1103";
    public const string DecisionAbsorbUndeclared = "US1104";
    public const string DecisionShapeInvalid = "US1105";
    public const string DecisionCrossUndeclared = "US1106";

    // ── US2xxx / US3xxx / US4xxx ────────────────────────────────────────
    public const string CrDangling = "US2001";
    public const string DeprecatedReferenced = "US2002";
    public const string EdgePeerUnknown = "US3001";
    public const string CircularReference = "US4001";
    public const string EdgeTypeUnmapped = "US4002";
    public const string EdgeTypeUnknown = "US4003";
    public const string FlowIntegrityViolation = "US4004";

    // ── US5xxx 发射面 ───────────────────────────────────────────────────
    public const string FlowNotDeclared = "US5001";
    public const string FlowStepUnmapped = "US5002";
    public const string SuccessCriterionUnknown = "US5003";
    public const string HierarchySchemaNotProducible = "US5004";
    public const string ChainTailAlreadyLinked = "US5005";
    public const string LocPlacementNotProducible = "US5006";
    public const string RefEdgeNotProducible = "US5007";
    public const string DomainSpecNotProducible = "US5008";
    public const string GatherSpecNotProducible = "US5009";

    // ── US6xxx 观测声明面 ───────────────────────────────────────────────
    public const string ObsEntityScopeUndefined = "US6001";
    public const string ObsEmitUndeclared = "US6002";
    public const string ObsDeclDuplicate = "US6003";
    public const string ObsEnumFieldNotRequired = "US6004";
    public const string ObsEventUnconsumed = "US6005";
    public const string ObsFileDuplicate = "US6006";
    public const string ObsKindUnknown = "US6007";

    // ── US70xx / US71xx / US72xx / US73xx ───────────────────────────────
    public const string DomainIdentityInvalid = "US7001";
    public const string DomainPlacementIdDuplicate = "US7002";
    public const string DomainSelectionUndeclared = "US7003";
    public const string DomainRetentionUndeclared = "US7004";
    public const string DomainShapeInvalid = "US7005";
    public const string GatherKindFieldMissing = "US7101";
    public const string GatherFamilyDuplicate = "US7102";
    public const string GatherShapingFamilyMissing = "US7103";
    public const string GatherShapingSummaryEmpty = "US7104";
    public const string GatherWildcardShapingMissing = "US7105";
    public const string GatherShapeInvalid = "US7106";
    public const string TableShapeInvalid = "US7201";
    public const string TableColumnInvalid = "US7202";
    public const string TableColumnDuplicate = "US7203";
    public const string RecordShapeInvalid = "US7204";
    public const string RecordTableUndeclared = "US7205";

    /// <summary><c>[Record] table=</c> 使用表容器名而非表 id（E-15 已收紧：error）。</summary>
    public const string RecordTableUsesContainer = "US7206";

    /// <summary>同名 <c>[Table]</c> 重复声明（产物 winner = FirstWins）。</summary>
    public const string TableIdDuplicate = "US7207";

    /// <summary>同名 <c>[Record]</c> 重复声明（产物 winner = FirstWins）。</summary>
    public const string RecordIdDuplicate = "US7208";
    public const string LlmContractOutputUndeclared = "US7301";
    public const string LlmContractInputUndeclared = "US7302";
    public const string LlmDeclShapeInvalid = "US7303";
    public const string LlmDeclSpaceUnknown = "US7304";

    // ── US74xx [KeySpace] ───────────────────────────────────────────────
    public const string KeyFamilyDuplicate = "US7401";
    public const string KeyFamilyTemplateMissing = "US7402";
    public const string KeySurfaceFamilyUnknown = "US7403";
    public const string KeyPortFamilyUnknown = "US7404";
    public const string KeyPolicyDuplicate = "US7405";
    public const string KeyLineUnparsed = "US7406";

    /// <summary>单文件装载无整包 <c>[KeySpace]</c>：预期态，不判红（跨层判据由整包编译承担）。</summary>
    public const string SingleFileNoKeySpace = "US7407";

    // ── US75xx [Command] ────────────────────────────────────────────────
    public const string CommandDuplicate = "US7501";
    public const string CommandChannelUnknown = "US7502";
    public const string CommandOpIncomplete = "US7503";
    public const string CommandKeyFamilyUnknown = "US7504";
    public const string CommandKeySurfaceUnknown = "US7505";
    public const string CommandKeyChainFieldUnknown = "US7506";
    public const string CommandPortUnknown = "US7507";
    public const string CommandOpRefUnknown = "US7508";
    public const string CommandDefaultUnknown = "US7509";
    public const string CommandLineUnparsed = "US7510";

    // ── US77xx [ReadModel] ──────────────────────────────────────────────
    public const string ReadModelDuplicate = "US7701";
    public const string ReadModelSourceMissing = "US7702";
    public const string ReadModelVersionUnknown = "US7703";
    public const string ReadModelTtlInvalid = "US7704";
    public const string ReadModelProjectorMissing = "US7705";
    public const string ReadModelLineUnparsed = "US7706";
    public const string ReadModelOpRefUnknown = "US7707";
    public const string ReadModelUnused = "US7708";
    public const string SegmentFieldDuplicate = "US7709";
    public const string SegmentTypeUnknown = "US7710";
    public const string ReadModelFieldsUndeclared = "US7711";
    public const string ReadModelFieldsEmpty = "US7712";
    public const string ReadModelFieldNoField = "US7713";
    public const string SegmentFieldMissing = "US7714";

    // ── US76xx Agent 适配面 ─────────────────────────────────────────────
    public const string SkillBackendUnknown = "US7601";
    public const string SkillBandUndeclared = "US7602";
    public const string SkillBaseBandMissing = "US7603";
    public const string PortFnUnknown = "US7604";
    public const string ToolTrackUnknown = "US7605";
    public const string BehaviorActionUnknown = "US7606";

    // ── US78xx 端口执行体归属 / US9xxx 能力未装配 ───────────────────────
    public const string PortSkillUndeclared = "US7801";
    public const string PortExecutorNotExclusive = "US7802";
    public const string PortKindUnknown = "US7803";
    public const string GraphBackendUnavailable = "US9001";
    public const string ObsBackendUnavailable = "US9002";

    // ── US81xx 关系声明面（[Relation] / [Invariant] / [Derive]）──────────
    public const string RelationDuplicate = "US8101";
    public const string RelationEdgeUndeclared = "US8102";
    public const string RelationEndpointMismatch = "US8103";
    public const string RelationFlowUnknown = "US8104";
    public const string RelationNamespaceUnknown = "US8105";
    public const string RelationFieldInvalid = "US8106";
    public const string RelationMountMissing = "US8107";
    public const string RelationEmitUndeclared = "US8108";
    public const string InvariantDuplicate = "US8109";
    public const string InvariantEdgeUndeclared = "US8110";
    public const string InvariantKindUnknown = "US8111";
    public const string InvariantOnViolationInvalid = "US8112";
    public const string InvariantScopeUndeclared = "US8113";
    public const string InvariantDuplicateShape = "US8114";
    public const string DeriveDuplicate = "US8115";
    public const string DeriveEndpointUndeclared = "US8116";
    public const string DeriveEdgeUndeclared = "US8117";
    public const string DeriveViaBroken = "US8118";
    public const string DeriveFieldInvalid = "US8119";
    public const string DeriveEdgeDeprecated = "US8120";
    public const string DeriveDuplicateShape = "US8121";
    public const string RelationPlanNotProducible = "US8122";
    public const string MountDuplicate = "US8123";
    public const string MountRelationUndeclared = "US8124";
    public const string MountTriggerUnknown = "US8125";
    public const string MountFieldInvalid = "US8126";
    public const string MountRelationUnbound = "US8127";

    /// <summary>[Invariant] guard.pre/guard.post/posture 非法（姿态不在受控词域或声明了姿态却无谓词）。</summary>
    public const string InvariantGuardInvalid = "US8128";

    // ── US82xx 成员级声明（[Const]）闸A ─────────────────────────────────
    /// <summary>[Const] target 形态非法（须「命名空间.类型」，至少两段）。</summary>
    public const string ConstTargetInvalid = "US8201";

    /// <summary>[Const] kind / valueType 不在受控词域。</summary>
    public const string ConstKindInvalid = "US8202";

    /// <summary>[Const] modifier 与 kind 组合非法（static record 不存在）。</summary>
    public const string ConstModifierInvalid = "US8203";

    /// <summary>[Const] 成员名非法（文法或 C# 关键字）。</summary>
    public const string ConstMemberInvalid = "US8204";

    /// <summary>[Const] 成员名重复（单元内或同 target 跨单元）。</summary>
    public const string ConstMemberDuplicate = "US8205";

    /// <summary>[Const] 同 target 多个单元的 kind/modifier 不一致。</summary>
    public const string ConstTargetConflict = "US8206";

    /// <summary>[Const] 值表达式不在白名单（禁 arbitrary code）。</summary>
    public const string ConstValueExpressionInvalid = "US8207";

    /// <summary>[Const] 续行无法解析为 <c>Name = Expr</c>。</summary>
    public const string ConstLineUnparsed = "US8208";

    // ── US83xx 产品图声明面（[Product] / [Face]）─────────────────────────
    /// <summary>[Product] 产品键重复声明。</summary>
    public const string ProductDuplicate = "US8301";

    /// <summary>[Product] 必填字段缺失或非法（level/role/out/idempotency）。</summary>
    public const string ProductFieldInvalid = "US8302";

    /// <summary>[Product] level= 指向未声明的 [Space] 层。</summary>
    public const string ProductLevelUndeclared = "US8303";

    /// <summary>[Product]/[Face] 声明行含无法解析的续行。</summary>
    public const string ProductLineUnparsed = "US8304";

    /// <summary>[Product] accepts= 形态非法（须 <c>owner:…,channel:…</c>）。</summary>
    public const string ProductAcceptsInvalid = "US8305";

    /// <summary>[Face] 面键重复声明。</summary>
    public const string FaceDuplicate = "US8306";

    /// <summary>[Face] product= 指向未声明的 [Product]。</summary>
    public const string FaceProductUndeclared = "US8307";

    /// <summary>[Face] fields= 不是产品出字段的子集。</summary>
    public const string FaceFieldNotSubset = "US8308";

    /// <summary>[Face] 声明了 fields= 但解析后为空。</summary>
    public const string FaceFieldsEmpty = "US8309";

    /// <summary>产品图不可产出（无任何 [Product]/[Face] 声明）。</summary>
    public const string ProductGraphNotProducible = "US8310";

    /// <summary>全量码表（声明序；序用于 README 生成与唯一性断言）。</summary>
    public static readonly IReadOnlyList<UsharpCodeSpec> All = new[]
    {
        E(SpaceDup, "Space", UsharpSeverity.Error, "同一空间 id 重复声明", "spaceId"),
        E(AxisDup, "Space", UsharpSeverity.Error, "同一空间内坐标重复声明", "spaceId, axis"),
        E(DeclEmpty, "Space", UsharpSeverity.Error, "声明为空（缺 id）", "prefix"),
        E(MatrixIdDuplicate, "Matrix", UsharpSeverity.Error, "同名 [Matrix] 重复声明", "matrixId"),
        E(FlowIdDuplicate, "Flow", UsharpSeverity.Error, "同名 [Flow] 重复声明", "flowId"),
        E(KeySpaceDuplicate, "KeySpace", UsharpSeverity.Error, "多个 [KeySpace] 声明（单键空间模型）", "keyspaceId"),
        E(DeclarationLoadFailure, "Declaration", UsharpSeverity.Error, "声明装载/回填/投影执行失败", "reason"),
        E(CallDepthTruncated, "ControlFlow", UsharpSeverity.Warning, "控制流子流嵌套超过 MaxCallDepth（已截断，后续子流未装配）", "flowId, limit"),
        E(ControlFlowSegmentUnparsed, "Binding", UsharpSeverity.Error, "控制流绑定段不可解析（无 '=' / 空键 / 空值）", "declaration, field, segment"),
        E(MatrixRowInvalid, "Matrix", UsharpSeverity.Error, "矩阵行形态非法", "matrixId, row"),
        E(AxisKindUnknown, "Space", UsharpSeverity.Error, "空间坐标值类非法（闸A 前移；矩阵装配同判据）", "spaceId, axis, kind"),
        E(SpaceKindUnknown, "Space", UsharpSeverity.Error, "[Space] kind= 取值非法", "spaceId, kind"),
        E(AxisLineUnparsed, "Space", UsharpSeverity.Error, "缩进块内坐标行不可解析", "spaceId, line"),
        E(CommandChannelMalformed, "Command", UsharpSeverity.Error, "[Command] channel= 不含 '.'", "action, channel"),
        E(CommandSpaceUndeclared, "Command", UsharpSeverity.Error, "[Command] 引用空间未声明", "action, space"),
        E(RuleEnabledInvalid, "Rule", UsharpSeverity.Error, "[Rule] enabled= 非法布尔", "ruleId, value"),
        E(ObsEnumSegmentInvalid, "Obs", UsharpSeverity.Error, "[Obs] enum= 段非法", "obsId, segment"),
        E(DocumentBodyLineUnparsed, "Document", UsharpSeverity.Error, "落库正文行不可解析", "documentId, line"),
        E(UnregisteredPrefix, "Decl", UsharpSeverity.Error, "声明行前缀未在任何已装载领域包注册（未装载 ⇒ 不识别）", "prefix"),

        E(SpaceUnknown, "Ref", UsharpSeverity.Error, "引用的空间未声明", "spaceId"),
        E(CoordUndeclared, "Ref", UsharpSeverity.Error, "引用坐标未在空间声明", "spaceId, key"),
        E(MatrixRowOutUndeclared, "Matrix", UsharpSeverity.Error, "矩阵行输出坐标未声明", "matrixId, key"),
        E(MatrixRowInputUndeclared, "Matrix", UsharpSeverity.Error, "矩阵行输入坐标未声明", "matrixId, key"),
        E(FlowStepUndeclared, "Flow", UsharpSeverity.Error, "流步骤键未声明", "flowId, key"),
        E(SubFlowUnknown, "Flow", UsharpSeverity.Error, "子流未声明", "flowId, subFlow"),
        E(RefShapeInvalid, "Ref", UsharpSeverity.Error, "[Ref] 形态非法", "refId"),
        E(SpaceHierarchyInvalid, "Hierarchy", UsharpSeverity.Error, "层图非法（形状/父层/可达/环）", "spaceId"),
        E(SubSpaceAxisUndeclared, "Hierarchy", UsharpSeverity.Error, "子空间坐标未在父空间声明", "child, key"),
        E(MatrixRowWriteOutOfSubSpace, "Hierarchy", UsharpSeverity.Error, "矩阵行越界写祖先层坐标", "layer, key"),
        E(ProcessorBindingNotSingle, "Hierarchy", UsharpSeverity.Error, "处理器绑定非单源", "layer"),
        E(SelfRecursiveBudgetMissing, "Hierarchy", UsharpSeverity.Warning, "自环层未声明 budget=", "layer"),
        E(RefScopeUndeclared, "Ref", UsharpSeverity.Error, "[Ref] scope= 未声明", "refId, scope"),
        E(ConnectEndpointUnknown, "Connect", UsharpSeverity.Error, "connects= 端点不可解析", "flowId, endpoint"),
        E(ConnectEdgeTypeEmpty, "Connect", UsharpSeverity.Error, "connects= 边类型为空", "flowId"),
        E(CrossBlockWriteDependencyMissing, "Connect", UsharpSeverity.Error, "跨块写依赖未声明", "layer, key"),
        E(PlanFieldInvalid, "ProcessPlan", UsharpSeverity.Error, "处理编排字段取值非法", "flowId, field"),
        E(BindingKeyDuplicate, "Binding", UsharpSeverity.Error, "控制流绑定段重复键", "bindingId, key"),

        E(TermZoneInvalid, "Decision", UsharpSeverity.Error, "词元 zone= 缺失或非法", "spaceId, term"),
        E(DecisionTermUndeclared, "Decision", UsharpSeverity.Error, "when= 引用未声明词元", "ruleId, term"),
        E(DecisionPriorityConflict, "Decision", UsharpSeverity.Error, "同层 priority 冲突", "layer"),
        E(DecisionAbsorbUndeclared, "Decision", UsharpSeverity.Error, "absorb= 悬空", "layer, label"),
        E(DecisionShapeInvalid, "Decision", UsharpSeverity.Error, "判据读法形态非法", "matrixId"),
        E(DecisionCrossUndeclared, "Decision", UsharpSeverity.Error, "cross.<名> 未声明", "ruleId, cross"),

        E(CrDangling, "CrRef", UsharpSeverity.Error, "跨库引用悬空", "refId, key"),
        E(DeprecatedReferenced, "CrRef", UsharpSeverity.Warning, "引用了废弃符号", "refId, key"),
        E(EdgePeerUnknown, "Edge", UsharpSeverity.Error, "边对端未声明", "edge"),
        E(CircularReference, "Graph", UsharpSeverity.Error, "引用图存在环", "node"),
        E(EdgeTypeUnmapped, "Graph", UsharpSeverity.Error, "边类型未映射", "edgeType"),
        E(EdgeTypeUnknown, "Graph", UsharpSeverity.Error, "边类型未知", "edgeType"),
        E(FlowIntegrityViolation, "Graph", UsharpSeverity.Error, "流完整性违规", "flowId"),

        E(FlowNotDeclared, "FlowEmit", UsharpSeverity.Error, "流未声明（不可产出）", "flowId"),
        E(FlowStepUnmapped, "FlowEmit", UsharpSeverity.Error, "步骤键无 [Node] 声明", "flowId, key"),
        E(SuccessCriterionUnknown, "FlowEmit", UsharpSeverity.Error, "success= 词元未知", "flowId, criterion"),
        E(HierarchySchemaNotProducible, "HierarchyEmit", UsharpSeverity.Error, "层次 schema 不可产出", "spaceId"),
        E(ChainTailAlreadyLinked, "ChainEmit", UsharpSeverity.Warning, "链尾重复连接（首个生效）", "flowId"),
        E(LocPlacementNotProducible, "LocEmit", UsharpSeverity.Error, "位置反解表不可产出", "matrixId"),
        E(RefEdgeNotProducible, "RefEdgeEmit", UsharpSeverity.Error, "归属边声明不可产出", "refId"),
        E(DomainSpecNotProducible, "DomainEmit", UsharpSeverity.Error, "节点域定义不可产出", "domainId"),
        E(GatherSpecNotProducible, "GatherEmit", UsharpSeverity.Error, "节点聚拢声明不可产出", "domainId"),

        E(ObsEntityScopeUndefined, "Obs", UsharpSeverity.Error, "entity 的 scope 未声明", "obsId"),
        E(ObsEmitUndeclared, "Obs", UsharpSeverity.Error, "emit= 事件未声明", "flowId, key"),
        E(ObsDeclDuplicate, "Obs", UsharpSeverity.Error, "观测声明重复", "obsId"),
        E(ObsEnumFieldNotRequired, "Obs", UsharpSeverity.Error, "enum= 字段不在 required= 集合", "obsId, field"),
        E(ObsEventUnconsumed, "Obs", UsharpSeverity.Warning, "事件无消费方", "obsId"),
        E(ObsFileDuplicate, "Obs", UsharpSeverity.Error, "kind=file 重复声明", "obsId"),
        E(ObsKindUnknown, "Obs", UsharpSeverity.Error, "kind= 取值非法", "obsId, kind"),

        E(DomainIdentityInvalid, "Domain", UsharpSeverity.Error, "域身份缺失或占位符不覆盖", "domainId"),
        E(DomainPlacementIdDuplicate, "Domain", UsharpSeverity.Error, "域位置 id 重复", "domainId, placementId"),
        E(DomainSelectionUndeclared, "Domain", UsharpSeverity.Error, "selection= 未声明", "domainId, selection"),
        E(DomainRetentionUndeclared, "Domain", UsharpSeverity.Error, "retention= 未声明", "domainId, retention"),
        E(DomainShapeInvalid, "Domain", UsharpSeverity.Error, "域声明形态非法", "domainId"),

        E(GatherKindFieldMissing, "Gather", UsharpSeverity.Error, "缺 kind-field", "gatherId"),
        E(GatherFamilyDuplicate, "Gather", UsharpSeverity.Error, "聚拢族名重复", "gatherId, family"),
        E(GatherShapingFamilyMissing, "Gather", UsharpSeverity.Error, "塑形行缺 family=", "gatherId"),
        E(GatherShapingSummaryEmpty, "Gather", UsharpSeverity.Error, "summary 为空列表", "gatherId, family"),
        E(GatherWildcardShapingMissing, "Gather", UsharpSeverity.Error, "缺通配塑形 *", "gatherId"),
        E(GatherShapeInvalid, "Gather", UsharpSeverity.Error, "聚拢声明形态非法", "gatherId"),

        E(TableShapeInvalid, "Table", UsharpSeverity.Error, "表声明形态非法", "tableId"),
        E(TableColumnInvalid, "Table", UsharpSeverity.Error, "列类型/标志非法", "tableId, column"),
        E(TableColumnDuplicate, "Table", UsharpSeverity.Error, "列名重复", "tableId, column"),
        E(RecordShapeInvalid, "Record", UsharpSeverity.Error, "记录声明形态非法", "recordId"),
        E(RecordTableUndeclared, "Record", UsharpSeverity.Error, "记录引用的表未声明", "recordId, tableId"),
        E(RecordTableUsesContainer, "Record", UsharpSeverity.Error, "[Record] table= 使用容器名而非表 id（表 id 与容器 id 不得混同）", "recordId, table"),
        E(TableIdDuplicate, "Table", UsharpSeverity.Error, "同名 [Table] 重复声明", "tableId"),
        E(RecordIdDuplicate, "Record", UsharpSeverity.Error, "同名 [Record] 重复声明", "recordId"),

        E(LlmContractOutputUndeclared, "LLMDecl", UsharpSeverity.Error, "output= 字段未在 O 区声明", "llmId, field"),
        E(LlmContractInputUndeclared, "LLMDecl", UsharpSeverity.Error, "input= 字段未在 I 区声明", "llmId, field"),
        E(LlmDeclShapeInvalid, "LLMDecl", UsharpSeverity.Error, "LLMDecl 形态非法", "llmId"),
        E(LlmDeclSpaceUnknown, "LLMDecl", UsharpSeverity.Error, "over= 空间未声明", "llmId, space"),

        E(KeyFamilyDuplicate, "KeySpace", UsharpSeverity.Error, "键族重复声明", "spaceId, family"),
        E(KeyFamilyTemplateMissing, "KeySpace", UsharpSeverity.Error, "键族缺 template=", "spaceId, family"),
        E(KeySurfaceFamilyUnknown, "KeySpace", UsharpSeverity.Error, "surface 族与所属族不一致", "spaceId, surface"),
        E(KeyPortFamilyUnknown, "KeySpace", UsharpSeverity.Error, "port 指向未声明族", "spaceId, port"),
        E(KeyPolicyDuplicate, "KeySpace", UsharpSeverity.Error, "policy 重复声明", "spaceId, policy"),
        E(KeyLineUnparsed, "KeySpace", UsharpSeverity.Error, "[KeySpace] 续行不可解析", "spaceId, line"),
        E(SingleFileNoKeySpace, "KeySpace", UsharpSeverity.Info,
            "[Domain] 已声明 coordinates 但本次编译无 [KeySpace]（单文件装载：跨层校验由整包编译承担）", "domainId"),

        E(CommandDuplicate, "Command", UsharpSeverity.Error, "[Command] 重复声明", "action"),
        E(CommandChannelUnknown, "Command", UsharpSeverity.Error, "channel 坐标未声明", "action, channel"),
        E(CommandOpIncomplete, "Command", UsharpSeverity.Error, "op 声明不完整", "action, op"),
        E(CommandKeyFamilyUnknown, "Command", UsharpSeverity.Error, "key 的族未声明", "action, family"),
        E(CommandKeySurfaceUnknown, "Command", UsharpSeverity.Error, "key 的面未声明", "action, surface"),
        E(CommandKeyChainFieldUnknown, "Command", UsharpSeverity.Error, "chain 字段未声明", "action, field"),
        E(CommandPortUnknown, "Command", UsharpSeverity.Error, "port 未声明", "action, port"),
        E(CommandOpRefUnknown, "Command", UsharpSeverity.Error, "key op= 未声明", "action, op"),
        E(CommandDefaultUnknown, "Command", UsharpSeverity.Error, "default=/fallback= 未声明", "action, op"),
        E(CommandLineUnparsed, "Command", UsharpSeverity.Error, "[Command] 续行不可解析", "action, line"),

        E(SkillBackendUnknown, "Agent.Skill", UsharpSeverity.Error, "[Skill] backend= 不在工具注册表", "skillId, backend"),
        E(SkillBandUndeclared, "Agent.Skill", UsharpSeverity.Error, "[Profile] bands= 未声明", "profileId, band"),
        E(SkillBaseBandMissing, "Agent.Skill", UsharpSeverity.Error, "[Profile] base=on 缺 bands", "profileId"),
        E(PortFnUnknown, "Agent.Port", UsharpSeverity.Error, "[Node] fn= 不在函数注册表", "nodeId, fn"),
        E(ToolTrackUnknown, "Agent.Tool", UsharpSeverity.Error, "轨 id 不在实现注册表", "face, track"),
        E(BehaviorActionUnknown, "Agent.Behavior", UsharpSeverity.Error, "[Rule] action= 不在 [Node]/动作注册表", "ruleId, action"),

        E(PortSkillUndeclared, "Agent.Port", UsharpSeverity.Error, "[Node] skill= 未声明", "nodeId, skill"),
        E(PortExecutorNotExclusive, "Agent.Port", UsharpSeverity.Error, "[Node] 执行体归属非恰一", "nodeId"),
        E(PortKindUnknown, "Agent.Port", UsharpSeverity.Error, "[Node] kind= 越界", "nodeId, kind"),

        E(ReadModelDuplicate, "ReadModel", UsharpSeverity.Error, "[ReadModel] 模型 id 重复声明", "modelId"),
        E(ReadModelSourceMissing, "ReadModel", UsharpSeverity.Error, "[ReadModel] source= 缺失（至少一个源键）", "modelId"),
        E(ReadModelVersionUnknown, "ReadModel", UsharpSeverity.Error, "[ReadModel] version= 不在词表", "modelId, version"),
        E(ReadModelTtlInvalid, "ReadModel", UsharpSeverity.Error, "[ReadModel] version=Ttl 但 ttl= 缺失或非正", "modelId, ttl"),
        E(ReadModelProjectorMissing, "ReadModel", UsharpSeverity.Error, "[ReadModel] projector= 缺失", "modelId"),
        E(ReadModelLineUnparsed, "ReadModel", UsharpSeverity.Error, "[ReadModel] 续行不可解析", "modelId, line"),
        E(ReadModelOpRefUnknown, "ReadModel", UsharpSeverity.Error, "[Command] op model= 指向未声明读模型", "action, op, model"),
        E(ReadModelUnused, "ReadModel", UsharpSeverity.Error, "[ReadModel] 声明无任何 op 引用（死声明）", "modelId"),
        E(SegmentFieldDuplicate, "Segment", UsharpSeverity.Error, "[Segment] field= 重复声明", "segmentId, field"),
        E(SegmentTypeUnknown, "Segment", UsharpSeverity.Error, "[Segment] type= 不在词表", "segmentId, type"),
        E(ReadModelFieldsUndeclared, "ReadModel", UsharpSeverity.Error, "[ReadModel] fields= 引用未声明段", "modelId, segment"),
        E(ReadModelFieldsEmpty, "ReadModel", UsharpSeverity.Error, "[ReadModel] fields= 解析后为空", "modelId"),
        E(ReadModelFieldNoField, "ReadModel", UsharpSeverity.Error, "[ReadModel] fields= 引用段缺 field=", "modelId, segment"),
        E(SegmentFieldMissing, "Segment", UsharpSeverity.Warning, "[Segment] 缺 field=（未进 DTO）", "segmentId"),

        E(GraphBackendUnavailable, "Graph", UsharpSeverity.Info, "图后端未装配", "-"),
        E(ObsBackendUnavailable, "Obs", UsharpSeverity.Info, "观测后端未装配", "-"),

        E(RelationDuplicate, "Relation", UsharpSeverity.Error, "[Relation] 操作名重复声明", "operation"),
        E(RelationEdgeUndeclared, "Relation", UsharpSeverity.Error, "[Relation] edge= 未声明", "operation, edge"),
        E(RelationEndpointMismatch, "Relation", UsharpSeverity.Error, "[Relation] src/tgt 未声明或与 [Edge] 不一致", "operation, src, tgt"),
        E(RelationFlowUnknown, "Relation", UsharpSeverity.Error, "[Relation] flow= 不存在或执行体不是 fn=relation_apply", "operation, flow"),
        E(RelationNamespaceUnknown, "Relation", UsharpSeverity.Error, "[Relation] namespace= 未声明", "operation, namespace"),
        E(RelationFieldInvalid, "Relation", UsharpSeverity.Error, "[Relation] duplicate/on_missing/arity 非法", "operation, field"),
        E(RelationMountMissing, "Relation", UsharpSeverity.Error, "归属边缺少 mount_* operation（或有多个）", "edge"),
        E(RelationEmitUndeclared, "Relation", UsharpSeverity.Error, "[Relation] emit= 事件未声明", "operation, emit"),
        E(InvariantDuplicate, "Invariant", UsharpSeverity.Error, "[Invariant] 名重复声明", "invariant"),
        E(InvariantEdgeUndeclared, "Invariant", UsharpSeverity.Error, "[Invariant] edge= 未声明", "invariant, edge"),
        E(InvariantKindUnknown, "Invariant", UsharpSeverity.Error, "[Invariant] kind= 非法", "invariant, kind"),
        E(InvariantOnViolationInvalid, "Invariant", UsharpSeverity.Error, "[Invariant] on_violation= 非法", "invariant, value"),
        E(InvariantScopeUndeclared, "Invariant", UsharpSeverity.Error, "[Invariant] scope= 未声明", "invariant, scope"),
        E(InvariantDuplicateShape, "Invariant", UsharpSeverity.Error, "[Invariant] (edge,kind,scope) 重复声明", "invariant, edge, kind"),
        E(DeriveDuplicate, "Derive", UsharpSeverity.Error, "[Derive] 名重复声明", "derive"),
        E(DeriveEndpointUndeclared, "Derive", UsharpSeverity.Error, "[Derive] from/to 未声明", "derive, type"),
        E(DeriveEdgeUndeclared, "Derive", UsharpSeverity.Error, "[Derive] via 含未声明边", "derive, edge"),
        E(DeriveViaBroken, "Derive", UsharpSeverity.Error, "[Derive] via 首尾不相接", "derive, via"),
        E(DeriveFieldInvalid, "Derive", UsharpSeverity.Error, "[Derive] op/normalize/order/empty 非法", "derive, field"),
        E(DeriveEdgeDeprecated, "Derive", UsharpSeverity.Error, "[Derive] via 含废弃边", "derive, edge"),
        E(DeriveDuplicateShape, "Derive", UsharpSeverity.Error, "[Derive] (from,to,via) 重复声明", "derive"),
        E(RelationPlanNotProducible, "RelationEmit", UsharpSeverity.Error, "关系计划不可产出", "reason"),
        E(MountDuplicate, "Mount", UsharpSeverity.Error, "[Mount] MountId 重复声明", "mount"),
        E(MountRelationUndeclared, "Mount", UsharpSeverity.Error, "[Mount] relation= 未在 [Relation] 声明", "mount, relation"),
        E(MountTriggerUnknown, "Mount", UsharpSeverity.Error, "[Mount] trigger= 未声明事件或不在内置触发集", "mount, trigger"),
        E(MountFieldInvalid, "Mount", UsharpSeverity.Error, "[Mount] stage/family/coalesce/posture/order 非法或出现禁用字段", "mount, field"),
        E(MountRelationUnbound, "Mount", UsharpSeverity.Error, "ensure 关系操作缺少 [Mount] 绑定", "relation"),
        E(InvariantGuardInvalid, "Invariant", UsharpSeverity.Error, "[Invariant] guard.pre/guard.post/posture 非法", "invariant, field"),
        E(ProductDuplicate, "Product", UsharpSeverity.Error, "[Product] 产品键重复声明", "product"),
        E(ProductFieldInvalid, "Product", UsharpSeverity.Error, "[Product] 必填字段缺失或非法（level/role/out/idempotency）", "product, field"),
        E(ProductLevelUndeclared, "Product", UsharpSeverity.Error, "[Product] level= 指向未声明层", "product, level"),
        E(ProductLineUnparsed, "Product", UsharpSeverity.Error, "[Product]/[Face] 续行不可解析", "decl, line"),
        E(ProductAcceptsInvalid, "Product", UsharpSeverity.Error, "[Product] accepts= 形态非法（须 owner:…,channel:…）", "product, accepts"),
        E(FaceDuplicate, "Face", UsharpSeverity.Error, "[Face] 面键重复声明", "face"),
        E(FaceProductUndeclared, "Face", UsharpSeverity.Error, "[Face] product= 未在 [Product] 声明", "face, product"),
        E(FaceFieldNotSubset, "Face", UsharpSeverity.Error, "[Face] fields= 不是产品出字段子集", "face, field"),
        E(FaceFieldsEmpty, "Face", UsharpSeverity.Error, "[Face] fields= 解析后为空", "face"),
        E(ProductGraphNotProducible, "ProductEmit", UsharpSeverity.Error, "产品图不可产出（无 [Product]/[Face]）", "reason"),
        E(IdentityEntityInvalid, "Entity", UsharpSeverity.Error, "[Entity] scope/container/cardinality/role/authority 缺失或非法", "entity, field"),
        E(IdentityAuthorityNotSingle, "Entity", UsharpSeverity.Error, "每个 [Entity] 在 family 表中的 authority=true 权威面须恰一个", "entity, count"),
        E(IdentityLexiconOverlap, "KeyLexicon", UsharpSeverity.Error, "[KeyLexicon] 词表互斥被破坏（同一词条出现在两个词表）", "lexicon, value"),
        E(IdentityShapeInvalid, "Shape", UsharpSeverity.Error, "[AnswerShape]/[WindowShape] 缺必填约束或 default 未声明", "shape, field"),
        E(IdentityPolicyInvalid, "KeyPolicy", UsharpSeverity.Error, "[KeyPolicy] entity/cardinality/writer/on_unresolvable 缺失", "policy, field"),
        E(IdentityBridgeInvalid, "KeyBridge", UsharpSeverity.Error, "[KeyBridge] from/to/rule 缺失或 reversible 未声明", "bridge, field"),
        E(IdentityAuthorityPending, "Entity", UsharpSeverity.Info, "[Entity] 尚无 authority=true 的 family（语义层已声明；语法族待接）", "entity"),
        E(IdentityLineUnparsed, "Identity", UsharpSeverity.Error, "身份声明行含无法解析/重复的字段段（[Entity]/[KeyLexicon]/[AnswerShape]/[WindowShape]/[KeyPolicy]/[KeyBridge]）", "declaration, segment"),
        E(ConstTargetInvalid, "Const", UsharpSeverity.Error, "[Const] target 须为「命名空间.类型」（至少两段）", "target"),
        E(ConstKindInvalid, "Const", UsharpSeverity.Error, "[Const] kind/valueType 不在受控词域", "target, field"),
        E(ConstModifierInvalid, "Const", UsharpSeverity.Error, "[Const] modifier 与 kind 组合非法", "target, modifier"),
        E(ConstMemberInvalid, "Const", UsharpSeverity.Error, "[Const] 成员名非法（文法或关键字）", "target, member"),
        E(ConstMemberDuplicate, "Const", UsharpSeverity.Error, "[Const] 成员名重复（单元内/同 target）", "target, member"),
        E(ConstTargetConflict, "Const", UsharpSeverity.Error, "[Const] 同 target 多单元 kind/modifier 不一致", "target"),
        E(ConstValueExpressionInvalid, "Const", UsharpSeverity.Error, "[Const] 值表达式不在白名单", "target, member, expression"),
        E(ConstLineUnparsed, "Const", UsharpSeverity.Error, "[Const] 续行无法解析为 Name = Expr", "target, line"),
    };

    /// <summary>全部码值（声明序）。</summary>
    public static readonly IReadOnlyList<string> Codes = All.Select(s => s.Code).ToArray();

    private static readonly IReadOnlyDictionary<string, UsharpCodeSpec> ByCode =
        All.ToDictionary(s => s.Code, StringComparer.Ordinal);

    /// <summary>按码取值取登记事实（未登记 ⇒ false）。</summary>
    /// <param name="code">诊断码。</param>
    /// <param name="spec">登记事实。</param>
    public static bool TryGet(string? code, out UsharpCodeSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    /// <summary>码值唯一性断言（重复即抛；供单测与装配期自检）。</summary>
    public static void AssertUnique()
    {
        var duplicate = All.GroupBy(s => s.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicate.Length > 0)
            throw new InvalidOperationException($"诊断码重复：{string.Join(", ", duplicate)}");
    }

    private static UsharpCodeSpec E(string code, string owner, UsharpSeverity severity, string message, string parameters)
        => new(code, owner, severity, message, parameters);
}
