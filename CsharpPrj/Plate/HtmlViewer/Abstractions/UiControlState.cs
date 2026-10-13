#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 交互态模型（L1 通用契约，L5 态外置）——展开/滚动/焦点/选中由**领域状态模型**给定并随渲染输入写入目标，
/// 使「态即数据」（<c>f(data,state)</c>），键控对齐时受控态不参与差分（自然保态）。
/// </summary>
public sealed record UiControlState(
    bool? Open = null,
    double? ScrollTop = null,
    double? ScrollLeft = null,
    bool? Focus = null,
    (int Start, int End)? Selection = null);
