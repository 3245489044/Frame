#nullable enable

using System.Collections.Generic;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// 投影策略坐标键剥离共享助手（D3 收敛）：Dock/Pixel/TabPage 三个投影策略此前各自
/// 复制「Remove(Left/Top/Width/Height)」四行，现统一经 <see cref="StripCoordinates"/>。
/// 语义：移除像素坐标键 = 坐标由原生/托管布局接管（Dock 布局引擎、Tab 显示矩形、无效尺寸），
/// 使快照差异比较仅针对非坐标属性，避免「目标像素坐标 vs 接管后物理坐标」恒差异。
/// </summary>
public static class ProjectionKeys
{
    public static void StripCoordinates(Dictionary<string, object?> dict)
    {
        dict.Remove(UIProtocol.Prop_Left);
        dict.Remove(UIProtocol.Prop_Top);
        dict.Remove(UIProtocol.Prop_Width);
        dict.Remove(UIProtocol.Prop_Height);
    }
}
