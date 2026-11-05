namespace Modules.Wiring.Model;

/// <summary>缺值处置（显式三态：fail 编译期红 / report 记账可见 / skip 记账跳过 / default 走声明默认）。</summary>
public enum OnMissing
{
    Fail,
    Skip,
    Report,
    Default,
}

/// <summary>汇聚模式（同一 inport ≥2 条 wire 时必须显式声明，否则 D4）。</summary>
public enum MergeMode
{
    None,
    First,
    Concat,
    Quorum,
}

/// <summary>
/// 接线：<c>From → To</c>（两端一律 <c>owner.port</c> 坐标）。
/// <para><see cref="Bridge"/>=true 表示跨 scope 的显式桥（否则 D8）。</para>
/// <para>本记录**不承载顺序**（顺序归上层 <c>[Flow]</c>，<c>SEP.DeclarativeProcessDisjoint</c>）。</para>
/// </summary>
public sealed record WireSpec(
    string Id,
    string From,
    string To,
    string? Adapter = null,
    string? Judge = null,
    OnMissing OnMissing = OnMissing.Fail,
    MergeMode Merge = MergeMode.None,
    string? Each = null,
    bool Bridge = false,
    string? Default = null);
