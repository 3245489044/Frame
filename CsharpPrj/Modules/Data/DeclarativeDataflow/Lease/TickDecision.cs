using System;

namespace Modules.DeclarativeDataflow.Lease;

/// <summary>
/// 每拍写判定（**纯函数，载荷无关**）—— 把"这一拍要不要写、写什么形态"从具体流水线里抽出来：
/// 输入是计划形态事实（有无根/树/流/叶子），输出是布尔裁决，不碰 IO、指纹、写通道。
/// <para>纪律：指纹相等性仍由宿主计算（内容指纹），本类只做"相同即跳过"的合成判定。</para>
/// </summary>
public static class TickDecision
{
    /// <summary>本拍无任何写目标 ⇒ 不写（且不得推进 baseline）。</summary>
    public static bool IsEmpty(bool hasRoots, bool hasTrees, bool hasStreams, bool hasLeaves)
        => !(hasRoots || hasTrees || hasStreams || hasLeaves);

    /// <summary>本拍只有易变叶子 ⇒ 心跳（Tick）路径（根/树/流一律不得走 Tick）。</summary>
    public static bool IsHeartbeat(bool hasRoots, bool hasTrees, bool hasStreams, bool hasLeaves)
        => !hasRoots && !hasTrees && !hasStreams && hasLeaves;

    /// <summary>稳定输入与易变输入均未变 ⇒ 短路跳过（零渲染、零写、零 baseline 推进）。</summary>
    public static bool SkipWhenUnchanged(bool stableUnchanged, bool volatileUnchanged)
        => stableUnchanged && volatileUnchanged;

    /// <summary>根投影拍（首挂载/视图切换）：根与区域/流/叶子合并为单一计划，根排首位。</summary>
    public static bool IsRootProjection(bool hasRoots) => hasRoots;
}
