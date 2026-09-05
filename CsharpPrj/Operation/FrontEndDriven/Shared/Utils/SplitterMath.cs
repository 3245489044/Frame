#nullable enable

using System;
using System.Windows.Forms;

namespace FrontEndDriven.Shared.Utils;

/// <summary>
/// Splitter 距离计算单一权威（E1 收敛）。
/// 方向感知基准——Vertical 沿宽、Horizontal 沿高，消除
/// 「比高还宽的横向分割 / 比宽还高的纵向分割」取错基准维度（Math.Max）的缺陷。
/// 消费方：LayoutProjector（反应式布局投影）与 Ports.CalculateSplitterDistance（快照收集）——
/// 二者此前各自实现、基准漂移（收集端残留 Math.Max），现统一经本方法结算。
/// </summary>
public static class SplitterMath
{
    /// <summary>由比例 + 方向 + 容器尺寸结算 SplitterDistance；尺寸 ≤ 0 返回 0。</summary>
    public static int Distance(double ratio, Orientation orientation, int width, int height)
    {
        int dimension = orientation == Orientation.Vertical ? width : height;
        return dimension > 0 ? (int)System.Math.Round(ratio * dimension) : 0;
    }

    /// <summary>
    /// 单面板矩形（Panel1/Panel2）统一结算：第一面板尺寸 = SplitterDistance，第二面板依栏宽偏移。
    /// E1·收敛：消解 LayoutSystem 此前「固定栏宽 4 手工近似」与 LayoutProjector「真实 Distance 投影」的口径分叉——
    /// 面板几何与 Distance 派生自同一比例 + 方向 + 栏宽，单一权威。坐标为面板自身（父区相对），由调用方加父原点。
    /// </summary>
    public static (int X, int Y, int Width, int Height) PanelRect(
        bool isFirst, double ratio, Orientation orientation, int width, int height, int barWidth)
    {
        barWidth = Math.Max(0, barWidth);
        int dist = Distance(ratio, orientation, width, height);
        if (orientation == Orientation.Vertical)
        {
            var p1 = (0, 0, dist, height);
            var p2 = (dist + barWidth, 0, Math.Max(0, width - dist - barWidth), height);
            return isFirst ? p1 : p2;
        }
        else
        {
            var p1 = (0, 0, width, dist);
            var p2 = (0, dist + barWidth, width, Math.Max(0, height - dist - barWidth));
            return isFirst ? p1 : p2;
        }
    }
}
