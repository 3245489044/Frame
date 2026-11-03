namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 守卫姿态（结构取值；单源，禁各处手写字面量）。
/// <para>
/// <c>require</c> = 违反即拒（新写默认）；<c>reduced</c> = 违反可见化但不拒（存量数据体检期）；
/// <c>best-effort</c> = 只记账（回退到「事后校验」形态）。
/// </para>
/// </summary>
public static class RelationGuardPostures
{
    /// <summary>违反即拒绝（fail-closed）。</summary>
    public const string Require = "require";

    /// <summary>违反只可见化（存量降档；不得用于新写路径长期运行）。</summary>
    public const string Reduced = "reduced";

    /// <summary>只记账（等价事后校验；显式降级，禁隐式）。</summary>
    public const string BestEffort = "best-effort";
}

/// <summary>
/// 守卫谓词词表（受控词；框架只实现结构事实谓词，域语义一律经声明面填值）。
/// </summary>
public static class RelationGuardPredicates
{
    /// <summary>源端点存在。</summary>
    public const string SourceExists = "source_exists";

    /// <summary>目标端点存在。</summary>
    public const string TargetExists = "target_exists";

    /// <summary>目标无其它父（单父约束）。</summary>
    public const string TargetSingleParent = "target_single_parent";

    /// <summary>源无其它目标（单目标约束）。</summary>
    public const string SourceSingleTarget = "source_single_target";

    /// <summary>不形成环（环判定由调用方以结构事实传入）。</summary>
    public const string Acyclic = "acyclic";

    /// <summary>目标边上不存在其它源（独占边）。</summary>
    public const string EdgeAbsent = "edge_absent";

    /// <summary>多对多（共享合法；只做预算可见化）。</summary>
    public const string ManyToMany = "many_to_many";
}

/// <summary>
/// 关系守卫（<c>[Invariant]</c> / <c>[Relation] guard.pre|guard.post</c> 的中性运行期形态）。
/// <para>
/// 判据载体 = 结构事实（计数 / 存在性 / 环），不含执行者叙述；未登记的谓词一律按不可判定处理
/// （不放行，也不默认通过）。
/// </para>
/// </summary>
/// <param name="Id">守卫标识（不变量 id 或 operation id）。</param>
/// <param name="Edge">边标签（守卫只作用于该关系）。</param>
/// <param name="Pre">写前谓词集（空 = 无前置断言）。</param>
/// <param name="Post">写后谓词集（空 = 无后置断言）。</param>
/// <param name="Posture">姿态（<see cref="RelationGuardPostures"/>）。</param>
/// <param name="OnViolation">违反处置（<c>reject</c> / <c>report</c> / <c>repair</c>）。</param>
public sealed record RelationGuard(
    string Id,
    string Edge,
    IReadOnlyList<string> Pre,
    IReadOnlyList<string> Post,
    string Posture = RelationGuardPostures.Require,
    string OnViolation = "reject");

/// <summary>
/// 守卫求值上下文（调用方在 step 前/后用已有结构事实装配；框架不自行遍历存储）。
/// </summary>
/// <param name="SourceKey">源端点键。</param>
/// <param name="TargetKey">目标端点键。</param>
/// <param name="SourceExists">源端点是否存在。</param>
/// <param name="TargetExists">目标端点是否存在。</param>
/// <param name="CreatesCycle">本次写入是否成环。</param>
/// <param name="EdgeExists">本次写入后该边是否已存在（写后求值用）。</param>
/// <param name="ExistingTargetParents">目标在该边上的既有入边源数（不含本次源）。</param>
/// <param name="ExistingSourceTargets">源在该边上的既有出边数（不含本次目标）。</param>
public sealed record RelationGuardContext(
    string SourceKey,
    string TargetKey,
    bool SourceExists = true,
    bool TargetExists = true,
    bool CreatesCycle = false,
    bool EdgeExists = false,
    int ExistingTargetParents = 0,
    int ExistingSourceTargets = 0);

/// <summary>守卫求值结果（违反项逐条可见；不可判定与违反共用出口但原因可辨）。</summary>
/// <param name="Satisfied">是否全部通过（含不可判定 ⇒ false）。</param>
/// <param name="Blocks">是否阻止写入（<c>require</c> 姿态且有违反/不可判定）。</param>
/// <param name="Violations">违反/不可判定清单（结构事实描述）。</param>
/// <param name="Posture">生效姿态（原样回传，供调用方记账）。</param>
public sealed record RelationGuardResult(
    bool Satisfied,
    bool Blocks,
    IReadOnlyList<string> Violations,
    string Posture);

/// <summary>守卫求值端口（纯函数；无 IO / 无状态）。</summary>
public interface IRelationGuardEvaluator
{
    /// <summary>写前求值（<see cref="RelationGuard.Pre"/>）。</summary>
    RelationGuardResult EvaluatePre(RelationGuard guard, RelationGuardContext context);

    /// <summary>写后求值（<see cref="RelationGuard.Post"/>）。</summary>
    RelationGuardResult EvaluatePost(RelationGuard guard, RelationGuardContext context);
}
