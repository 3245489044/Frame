using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation.ContextBinding;

namespace Share.Conversation.Rendering;

/// <summary>
/// 渲染面（渲染块编排单位）—— 与缓存带（<see cref="CacheBand"/>）对齐的声明式编排单位。
/// <para>面 ≠ 消息通道（<c>MessageChannel</c>）：通道决定「消息去哪」，面决定「块按什么序编排」。</para>
/// </summary>
public enum RenderSurface
{
    /// <summary>头提示层块（<c>prompt:*</c> 段；CacheBand B0..B3 = 前缀层）—— 缓存稳定序，置顶。</summary>
    HeadPrompt,

    /// <summary>操作流面板（非气泡；<c>MessageChannel.Operation</c> 的 task 创建/管理命令与回执）。</summary>
    Operation,

    /// <summary>对话轮分组（气泡流；user/assistant/过程节点）。</summary>
    Dialogue,
}

/// <summary>渲染面声明（面 + 缓存带）。</summary>
/// <param name="Surface">渲染面。</param>
/// <param name="Band">该面承载内容的缓存带（编排序 = 本带升序；变更频率升序）。</param>
public sealed record RenderSurfaceSpec(RenderSurface Surface, CacheBand Band);

/// <summary>
/// 渲染面目录 —— 渲染块顺序的**单一权威**：按 <see cref="CacheBand"/> 升序（变更频率升序）编排，
/// 同带按声明序（与 <see cref="ContextBinding.PromptSegmentCatalog"/> 同律）。
/// <para>渲染器只按本表（或宿主显式覆盖）迭代，**禁在渲染器内硬编码块拼接顺序**；
/// 顺序一变只改本表一处（对齐「一处声明 → 多处派生」）。</para>
/// </summary>
public static class RenderSurfaceCatalog
{
    /// <summary>面声明（有序；顺序律 = CacheBand 升序 + 同带声明序）。</summary>
    public static readonly IReadOnlyList<RenderSurfaceSpec> Fixed = new[]
    {
        // 头提示层：前缀层 B0..B3 → 最先（B0 置顶）。
        new RenderSurfaceSpec(RenderSurface.HeadPrompt, CacheBand.B0),
        // 操作流 / 对话：均为 append-only 流水（会话级 + 逐轮追加）→ B4。
        // 同带并列时的次序由声明序裁决：控制面（命令与回执）先于对话气泡。
        new RenderSurfaceSpec(RenderSurface.Operation, CacheBand.B4),
        new RenderSurfaceSpec(RenderSurface.Dialogue, CacheBand.B4),
    };

    /// <summary>面编排序（Band 升序；稳定排序 ⇒ 同带保声明序）。渲染器的默认迭代序。</summary>
    public static IReadOnlyList<RenderSurface> Order { get; } = Fixed
        .OrderBy(s => (int)s.Band)
        .Select(s => s.Surface)
        .ToArray();

    /// <summary>面的声明位置（未声明 → int.MaxValue，排末位，不抛）。</summary>
    public static int IndexOf(RenderSurface surface)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (Order[i] == surface) return i;
        }
        return int.MaxValue;
    }

    /// <summary>面承载内容的缓存带（未声明 → B5 兜底）。</summary>
    public static CacheBand BandOf(RenderSurface surface)
    {
        foreach (var spec in Fixed)
        {
            if (spec.Surface == surface) return spec.Band;
        }
        return CacheBand.B5;
    }

    /// <summary>面完备性判据（可机械复算）：每面恰声明一次（重复/缺失即违规）。</summary>
    public static IReadOnlyList<string> Validate()
    {
        var violations = new List<string>();
        foreach (var surface in Enum.GetValues<RenderSurface>())
        {
            var count = Fixed.Count(s => s.Surface == surface);
            if (count == 0) violations.Add($"渲染面 {surface} 未声明（缺编排位）");
            else if (count > 1) violations.Add($"渲染面 {surface} 重复声明 {count} 次");
        }
        return violations;
    }
}
