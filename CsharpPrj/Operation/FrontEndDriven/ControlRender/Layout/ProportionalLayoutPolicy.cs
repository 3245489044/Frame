#nullable enable

using System;
using System.Collections.Generic;
using ECS.Core;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 「比例 + 最小像素」双约束分配策略（纯函数，无 I/O，可单测）：
///   分配宽 = max(round(比例 × 父宽), round(最小宽 DIP × DeviceDpi / 96))。
/// 超额回收规则：
///   1) 同一父容器内按垂直重叠划分「行带」（band）；
///   2) 每带按声明左序做顺序排布，间隙取名义间隙（名义左边界差 − 名义宽）；
///   3) 带内总宽超出父宽时，只从 ShrinkPriority &gt; 0 的弹性列按优先级降序回收，最低回收至其最小像素宽；
///      刚性列（ShrinkPriority = 0）永不低于下界；
///   4) 回收后仍超出 → 记录 overflow（不换行、不裁字、不侵占相邻带）。
/// </summary>
public static class ProportionalLayoutPolicy
{
    /// <summary>DIP 参考 DPI（配置里的 minWidth/minHeight 以 96 dpi 为基准）。</summary>
    public const int ReferenceDpi = 96;

    public const string EffectiveRatio = "ratio";
    public const string EffectiveMin = "min";
    public const string EffectiveShrunk = "shrunk";
    public const string EffectiveOverflow = "overflow";

    /// <summary>DIP → 物理像素（按实际 DeviceDpi 折算，不写死像素）。</summary>
    public static int ScaleDip(int dip, int dpi)
    {
        if (dip <= 0)
            return 0;

        var effectiveDpi = dpi <= 0 ? ReferenceDpi : dpi;
        return (int)Math.Round(dip * effectiveDpi / (double)ReferenceDpi, MidpointRounding.AwayFromZero);
    }

    /// <summary>名义比例尺寸（沿用 LayoutSystem 既有取整语义，保证无最小约束时逐值不变）。</summary>
    public static int Nominal(float ratio, int extent) => (int)(ratio * extent);

    public readonly struct Slot
    {
        public Slot(
            Entity entity,
            float left,
            float top,
            float width,
            float height,
            int minWidthDip,
            int minHeightDip,
            int shrinkPriority)
        {
            Entity = entity;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            MinWidthDip = minWidthDip;
            MinHeightDip = minHeightDip;
            ShrinkPriority = shrinkPriority;
        }

        public Entity Entity { get; }
        public float Left { get; }
        public float Top { get; }
        public float Width { get; }
        public float Height { get; }
        public int MinWidthDip { get; }
        public int MinHeightDip { get; }
        public int ShrinkPriority { get; }
    }

    public readonly struct Result
    {
        public Result(
            Entity entity,
            int left,
            int top,
            int width,
            int height,
            int nominalWidth,
            int minWidthPx,
            string effective,
            int overflowPx)
        {
            Entity = entity;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            NominalWidth = nominalWidth;
            MinWidthPx = minWidthPx;
            Effective = effective;
            OverflowPx = overflowPx;
        }

        public Entity Entity { get; }
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int NominalWidth { get; }
        public int MinWidthPx { get; }
        public string Effective { get; }
        public int OverflowPx { get; }
    }

    /// <summary>对同一父容器的比例子实体做双约束分配；返回每个实体的父相对矩形与生效约束记录。</summary>
    public static IReadOnlyList<Result> Allocate(
        int parentWidth,
        int parentHeight,
        int dpi,
        IReadOnlyList<Slot> slots)
    {
        if (slots.Count == 0 || parentWidth <= 0 || parentHeight <= 0)
            return Array.Empty<Result>();

        int count = slots.Count;
        var nominalLeft = new int[count];
        var nominalTop = new int[count];
        var nominalWidth = new int[count];
        var nominalHeight = new int[count];
        var minWidthPx = new int[count];
        var minHeightPx = new int[count];

        for (int i = 0; i < count; i++)
        {
            var slot = slots[i];
            nominalLeft[i] = Nominal(slot.Left, parentWidth);
            nominalTop[i] = Nominal(slot.Top, parentHeight);
            nominalWidth[i] = Nominal(slot.Width, parentWidth);
            nominalHeight[i] = Nominal(slot.Height, parentHeight);
            minWidthPx[i] = ScaleDip(slot.MinWidthDip, dpi);
            minHeightPx[i] = ScaleDip(slot.MinHeightDip, dpi);
        }

        var results = new Result[count];
        var assigned = new bool[count];

        foreach (var band in BuildBands(nominalTop, nominalHeight, nominalLeft))
        {
            AllocateBand(band, slots, nominalLeft, nominalTop, nominalWidth, nominalHeight,
                minWidthPx, minHeightPx, parentWidth, results, assigned);
        }

        // 理论上所有索引都会被某个带覆盖；防御：未分配的按单槽带兜底。
        for (int i = 0; i < count; i++)
        {
            if (assigned[i])
                continue;

            results[i] = new Result(slots[i].Entity,
                nominalLeft[i], nominalTop[i],
                Math.Max(nominalWidth[i], minWidthPx[i]),
                Math.Max(nominalHeight[i], minHeightPx[i]),
                nominalWidth[i], minWidthPx[i], EffectiveRatio, 0);
        }

        return results;
    }

    /// <summary>按垂直重叠聚成行带（交集 &gt; 较矮者高度的一半视为同一带）。</summary>
    private static IReadOnlyList<IReadOnlyList<int>> BuildBands(
        int[] nominalTop, int[] nominalHeight, int[] nominalLeft)
    {
        var order = new List<int>(nominalTop.Length);
        for (int i = 0; i < nominalTop.Length; i++)
            order.Add(i);

        order.Sort((a, b) =>
        {
            int cmp = nominalTop[a].CompareTo(nominalTop[b]);
            return cmp != 0 ? cmp : nominalLeft[a].CompareTo(nominalLeft[b]);
        });

        var bands = new List<IReadOnlyList<int>>();
        var current = new List<int>();
        int bandTop = 0;
        int bandBottom = 0;

        foreach (int index in order)
        {
            int top = nominalTop[index];
            int height = Math.Max(0, nominalHeight[index]);
            int bottom = top + height;

            bool sameBand = current.Count > 0
                && Math.Min(bandBottom, bottom) - Math.Max(bandTop, top)
                    > Math.Min(bandBottom - bandTop, height) / 2;

            if (!sameBand)
            {
                if (current.Count > 0)
                    bands.Add(current);
                current = new List<int>();
                bandTop = top;
                bandBottom = bottom;
            }
            else
            {
                bandTop = Math.Min(bandTop, top);
                bandBottom = Math.Max(bandBottom, bottom);
            }

            current.Add(index);
        }

        if (current.Count > 0)
            bands.Add(current);

        return bands;
    }

    private static void AllocateBand(
        IReadOnlyList<int> band,
        IReadOnlyList<Slot> slots,
        int[] nominalLeft,
        int[] nominalTop,
        int[] nominalWidth,
        int[] nominalHeight,
        int[] minWidthPx,
        int[] minHeightPx,
        int parentWidth,
        Result[] results,
        bool[] assigned)
    {
        int count = band.Count;
        var widths = new int[count];
        var effective = new string[count];

        for (int k = 0; k < count; k++)
        {
            int index = band[k];
            widths[k] = Math.Max(nominalWidth[index], minWidthPx[index]);
            effective[k] = widths[k] > nominalWidth[index] ? EffectiveMin : EffectiveRatio;
        }

        // 名义间隙（以名义左边界和名义宽推导，保持设计间距）。
        var gaps = new int[count];
        for (int k = 0; k < count - 1; k++)
        {
            int index = band[k];
            int next = band[k + 1];
            gaps[k] = Math.Max(0, nominalLeft[next] - (nominalLeft[index] + nominalWidth[index]));
        }

        int start = nominalLeft[band[0]];
        long total = start;
        for (int k = 0; k < count; k++)
            total += widths[k] + gaps[k];

        long deficit = total - parentWidth;

        if (deficit > 0)
        {
            // 超额回收：弹性列按优先级降序让出空间，最低到其最小像素宽。
            var elastic = new List<int>();
            for (int k = 0; k < count; k++)
            {
                if (slots[band[k]].ShrinkPriority > 0)
                    elastic.Add(k);
            }

            elastic.Sort((a, b) =>
            {
                int cmp = slots[band[b]].ShrinkPriority.CompareTo(slots[band[a]].ShrinkPriority);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            foreach (int k in elastic)
            {
                if (deficit <= 0)
                    break;

                int floor = minWidthPx[band[k]];
                int reducible = widths[k] - floor;
                if (reducible <= 0)
                    continue;

                int take = (int)Math.Min(deficit, reducible);
                widths[k] -= take;
                deficit -= take;
                effective[k] = EffectiveShrunk;
            }
        }
        else if (deficit < 0)
        {
            // 富余空间交给弹性「吸收器」，使带右缘与父右缘对齐（无弹性列则保持设计间距）。
            int absorber = -1;
            for (int k = 0; k < count; k++)
            {
                if (slots[band[k]].ShrinkPriority <= 0)
                    continue;
                if (absorber < 0 || slots[band[k]].ShrinkPriority < slots[band[absorber]].ShrinkPriority)
                    absorber = k;
            }

            if (absorber >= 0)
                widths[absorber] += (int)(-deficit);
        }

        int overflow = deficit > 0 ? (int)deficit : 0;

        // 顺序排布：起点 = 首列名义左边界；其后各列紧接前列 + 名义间隙。
        int cursor = start;
        for (int k = 0; k < count; k++)
        {
            int index = band[k];
            int left = cursor;
            int height = Math.Max(nominalHeight[index], minHeightPx[index]);

            results[index] = new Result(
                slots[index].Entity,
                left,
                nominalTop[index],
                widths[k],
                height,
                nominalWidth[index],
                minWidthPx[index],
                overflow > 0 ? EffectiveOverflow : effective[k],
                overflow);

            assigned[index] = true;
            cursor = left + widths[k] + gaps[k];
        }
    }
}
