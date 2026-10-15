#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 浮层受控态（P-A 态外置，I1）——「某个浮层是否打开」是一等**模型状态**，非 DOM 副产物。
/// 渲染 <c>f(data,state)</c> 由本态决定浮层内容/开闭；刷新拍回投本态使浮层跨刷新保持。
/// <para>纯数据（零领域语义）：<paramref name="Key"/> = 浮层身份键（如 task/history 键），
/// <paramref name="Open"/> = 开闭；<see cref="Closed"/> 为缺省收敛值。</para>
/// </summary>
public sealed record UiOverlayState(string? Key, bool Open)
{
    /// <summary>关闭态（无浮层）。</summary>
    public static UiOverlayState Closed { get; } = new(null, false);
}
