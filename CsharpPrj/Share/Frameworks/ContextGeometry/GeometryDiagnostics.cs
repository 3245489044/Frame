using ContractFramework.Codes;

namespace Share.ContextGeometry;

/// <summary>几何诊断码单源（禁在各处手写字面量）。</summary>
public static class GeometryDiagnostics
{
    /// <summary>前缀漂移（段级变化可见）。</summary>
    public const string PrefixDrift = "geom_prefix_drift";

    /// <summary>未知/禁入缓存带（显式拒绝，禁静默当 B3）。</summary>
    public const string UnknownBand = "geom_unknown_band";

    /// <summary>重复段键（保留先序，后者丢弃并诊断）。</summary>
    public const string DuplicateKey = "geom_duplicate_key";

    /// <summary>重复共享块（同内容只发射一次）。</summary>
    public const string DuplicateBlock = "geom_duplicate_block";

    /// <summary>provider 未给缓存用量（Unknown，禁按 0 计）。</summary>
    public const string UsageMissing = "geom_usage_missing";

    /// <summary>估计前缀 token 不可用（比率不可算，非未命中）。</summary>
    public const string EstimateMissing = "geom_estimate_missing";

    /// <summary>估计前缀 token 偏小导致命中率 &gt; 1（显式可见，禁压成 1）。</summary>
    public const string RatioOverflow = "geom_ratio_overflow";

    /// <summary>快照缺失（无法继承冻结字节）。</summary>
    public const string SnapshotMissing = "geom_snapshot_missing";

    /// <summary>keepalive 重放（可观测）。</summary>
    public const string KeepAliveReplay = "geom_keepalive_replay";

    /// <summary>委托被排除清单命中（内联执行）。</summary>
    public const string DelegateExcluded = "geom_delegate_excluded";

    /// <summary>工具面按声明被裁掉（tools_off；显式可见，不静默）。</summary>
    public const string ToolFaceHidden = "geom_toolface_hidden";

    /// <summary>工具面结构性违约（exposes=false 却仍有 schema）。</summary>
    public const string ToolFaceViolation = "geom_toolface_violation";

    /// <summary>工具数观测非法（负数 ⇒ 显式违约，禁归零伪造"零工具"）。</summary>
    public const string ToolFaceCountInvalid = "geom_toolface_count_invalid";

    /// <summary>工具面冻结字节漂移（当前工具面 ≠ 快照冻结面；拒绝静默复用）。</summary>
    public const string ToolFaceDrift = "geom_toolface_drift";

    /// <summary>无前缀（装配点未注入段集合；不得伪造空前缀）。</summary>
    public const string PrefixAbsent = "geom_prefix_absent";

    /// <summary>投影事件序非法（角色/内容缺失；显式诊断，不静默跳过语义）。</summary>
    public const string ProjectionInvalid = "geom_projection_invalid";

    /// <summary>观测值非法（负数等；显式诊断，禁归零伪造成"零消耗"）。</summary>
    public const string InvalidObservation = "geom_invalid_observation";

    /// <summary>受控码集（单一实现 = <see cref="CodeSet"/>；新增码必须登记，否则不可判定）。</summary>
    public static readonly CodeSet Codes = new(
        "context-geometry",
        PrefixDrift, UnknownBand, DuplicateKey, DuplicateBlock,
        UsageMissing, EstimateMissing, RatioOverflow, SnapshotMissing,
        KeepAliveReplay, DelegateExcluded, ToolFaceHidden, ToolFaceViolation,
        ToolFaceCountInvalid, ToolFaceDrift, PrefixAbsent, ProjectionInvalid,
        InvalidObservation);
}
