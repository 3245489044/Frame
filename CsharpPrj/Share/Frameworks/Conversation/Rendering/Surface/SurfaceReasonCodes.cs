#nullable enable

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 渲染/概览可见性的**原因码单源**（受控词；禁各层手写字面量）。
/// <para>登记面：<c>Config\ui\ui-reason-loc.json</c>（码 → 文案坐标）；未登记码按“未知”占位渲染（fail-visible）。</para>
/// </summary>
public static class SurfaceReasonCodes
{
    /// <summary>字段应有但值为空（真实缺值）。</summary>
    public const string MissingValue = "missing-value";

    /// <summary>该 kind 的声明键集内没有此键（不适用，不是缺值）。</summary>
    public const string KindHasNoSuchKey = "kind-has-no-such-key";

    /// <summary>structured 单元的值形似 JSON 但解析失败。</summary>
    public const string StructuredParseFailed = "structured-parse-failed";

    /// <summary>声明面判定不适用（不渲染）。</summary>
    public const string NotApplicable = "not-applicable";

    /// <summary>渲染声明不可用（装载失败/未装配）。</summary>
    public const string DeclarationsUnavailable = "declarations-unavailable";

    /// <summary>帧计划计数不可得（未合成/旧构造点）。</summary>
    public const string FrameAccountUnavailable = "frame-account-unavailable";

    /// <summary>未呈现账本不可得（P4 查询未接线）。</summary>
    public const string NotPresentedLedgerUnavailable = "not-presented-ledger-unavailable";

    /// <summary>该视图未物化 canon 账本（不适用，不是缺值）。</summary>
    public const string CanonNotMaterialized = "canon-not-materialized";

    /// <summary>恒等式不可判定（分量缺失）。</summary>
    public const string BalanceUndetermined = "balance-undetermined";

    /// <summary>视角过滤未声明。</summary>
    public const string LensUndeclared = "lens-undeclared";

    /// <summary>受控词全集（门禁与测试比对用）。</summary>
    public static readonly string[] All =
    {
        MissingValue, KindHasNoSuchKey, StructuredParseFailed, NotApplicable, DeclarationsUnavailable,
        FrameAccountUnavailable, NotPresentedLedgerUnavailable, CanonNotMaterialized, BalanceUndetermined,
        LensUndeclared,
    };
}
