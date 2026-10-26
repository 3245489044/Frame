#nullable enable

using System;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 浮层宿主（通用件）—— 把「当前是否打开浮层 / 打开什么 / 如何收起」收敛为**单槽受控态**：
/// 同一宿主任意时刻至多一个浮层（后写胜出），空内容即收起，状态外置可复用（刷新拍不用重传 HTML）。
/// <para>
/// 纪律：① 空/空白内容 ⇒ 关闭（禁止「空壳浮层」占位）；② 单写入口 <see cref="Show"/>，写即替换；
/// ③ 只存 HTML 片段，不感知领域（taskKey/窗口类型由调用方编码在片段内）。
/// 领域侧只需：打开时给片段，关闭时给 null。
/// </para>
/// </summary>
public sealed class HtmlOverlayHost
{
    private readonly UiStateSlot _slot;

    /// <param name="state">共享受控态容器（与视图选择态同源）。</param>
    /// <param name="slotKey">槽键（全局唯一）。</param>
    public HtmlOverlayHost(UiViewState state, string slotKey)
        => _slot = new UiStateSlot(state ?? throw new ArgumentNullException(nameof(state)), slotKey);

    /// <summary>当前浮层 HTML（null = 未打开）。</summary>
    public string? Current => _slot.Value;

    /// <summary>是否已打开。</summary>
    public bool IsOpen => _slot.Has;

    /// <summary>展示/替换浮层；null 或空白 ⇒ 收起（幂等）。返回写入后的当前值。</summary>
    public string? Show(string? html)
    {
        _slot.Value = html;
        return _slot.Value;
    }

    /// <summary>收起浮层（幂等）。</summary>
    public void Close() => _slot.Clear();
}
