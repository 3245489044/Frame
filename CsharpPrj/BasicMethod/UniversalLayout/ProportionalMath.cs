#nullable enable
using System;

namespace UniversalLayoutMath
{
    public static class ProportionalMath
    {
        public static int RatioToPixel(int parentSize, double ratio)
        {
            if (parentSize <= 0) return 0;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            return (int)Math.Round(parentSize * ratio);
        }

        public static double PixelToRatio(int parentSize, int pixel)
        {
            if (parentSize <= 0) return 0;
            if (pixel < 0) pixel = 0;
            if (pixel > parentSize) pixel = parentSize;
            return (double)pixel / parentSize;
        }

        public static (int X, int Y, int Width, int Height) CalculateProportionalBounds(
            int parentWidth, int parentHeight,
            double left, double top,
            double? widthRatio, double? heightRatio,
            int defaultX, int defaultY, int defaultWidth, int defaultHeight)
        {
            int x = left >= 0 && left <= 1
                ? RatioToPixel(parentWidth, left)
                : defaultX;
            int y = top >= 0 && top <= 1
                ? RatioToPixel(parentHeight, top)
                : defaultY;
            int w = widthRatio.HasValue && widthRatio.Value >= 0 && widthRatio.Value <= 1
                ? RatioToPixel(parentWidth, widthRatio.Value)
                : defaultWidth;
            int h = heightRatio.HasValue && heightRatio.Value >= 0 && heightRatio.Value <= 1
                ? RatioToPixel(parentHeight, heightRatio.Value)
                : defaultHeight;

            if (w <= 0 && !widthRatio.HasValue) w = defaultWidth;
            if (h <= 0 && !heightRatio.HasValue) h = defaultHeight;
            if (w <= 0) w = 1;
            if (h <= 0) h = 1;

            return (x, y, w, h);
        }

        public static (int X, int Y, int Width, int Height) CalculateProportionalBounds(
            int parentWidth, int parentHeight,
            double left, double top,
            double? widthRatio = null, double? heightRatio = null)
        {
            return CalculateProportionalBounds(
                parentWidth, parentHeight,
                left, top, widthRatio, heightRatio,
                0, 0, 100, 100);
        }

        public static bool IsValidProportionalPosition(double left, double top, double width, double height)
        {
            return left >= 0 && left <= 1 &&
                   top >= 0 && top <= 1 &&
                   width > 0 && width <= 1 &&
                   height > 0 && height <= 1 &&
                   left + width <= 1 &&
                   top + height <= 1;
        }

        public static (double Left, double Top, double Width, double Height) ClampProportionalPosition(
            double left, double top, double width, double height)
        {
            left = Math.Max(0, Math.Min(1, left));
            top = Math.Max(0, Math.Min(1, top));
            width = Math.Max(0.01, Math.Min(1 - left, width));
            height = Math.Max(0.01, Math.Min(1 - top, height));
            return (left, top, width, height);
        }

        public static (int X, int Y, int Width, int Height) CalculateProportionalBoundsFromRightBottom(
            int parentWidth, int parentHeight,
            double right, double bottom,
            double? widthRatio, double? heightRatio,
            int defaultX, int defaultY, int defaultWidth, int defaultHeight)
        {
            int w = widthRatio.HasValue && widthRatio.Value >= 0 && widthRatio.Value <= 1
                ? RatioToPixel(parentWidth, widthRatio.Value)
                : defaultWidth;
            int h = heightRatio.HasValue && heightRatio.Value >= 0 && heightRatio.Value <= 1
                ? RatioToPixel(parentHeight, heightRatio.Value)
                : defaultHeight;

            if (w <= 0) w = defaultWidth;
            if (h <= 0) h = defaultHeight;

            int x = right >= 0 && right <= 1
                ? parentWidth - RatioToPixel(parentWidth, right) - w
                : defaultX;
            int y = bottom >= 0 && bottom <= 1
                ? parentHeight - RatioToPixel(parentHeight, bottom) - h
                : defaultY;

            return (x, y, w, h);
        }

    }
}
