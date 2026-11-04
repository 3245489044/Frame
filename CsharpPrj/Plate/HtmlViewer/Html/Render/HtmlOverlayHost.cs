#nullable enable

using System;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 浮层宿主（通用件）—— 把「当前是否打开浮层 / 打开什么 / 如何收起」收敛为**单槽受控态**。
/// <para><b>已弃用</b>：生产浮层受控态走 <c>UiOverlayRegistry</c>；本类保留仅为兼容编译，禁止新增调用。</para>
/// </summary>
[Obsolete("生产浮层受控态走 UiOverlayRegistry；本类无调用方，勿新增引用。")]
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
