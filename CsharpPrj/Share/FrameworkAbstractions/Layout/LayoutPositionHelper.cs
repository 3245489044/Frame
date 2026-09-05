#nullable enable

namespace FrameworkAbstractions.Layout
{
    public static class LayoutPositionHelper
    {
        private static bool IsProportionalValue(double value) => value >= 0 && value <= 1;

        private static bool IsPixelValue(double value) => value > 1;

        public static (double Left, double Top) ConvertBottomRightToNegative(
            double left, double top, double right, double bottom)
        {
            if (bottom >= 0 && top < 0)
            {
                top = -bottom;
            }

            if (right >= 0 && left < 0)
            {
                left = -right;
            }

            return (left, top);
        }

        public static (double Left, double Top, double Width, double Height, double Bottom, double Right) ConvertPixelsToProportional(
            int containerWidth, int containerHeight,
            double left, double top, double width, double height,
            double bottom = -1, double right = -1)
        {
            if (containerWidth <= 0) containerWidth = 1;
            if (containerHeight <= 0) containerHeight = 1;

            if (IsPixelValue(left)) left = left / containerWidth;
            if (IsPixelValue(top)) top = top / containerHeight;
            if (IsPixelValue(width)) width = width / containerWidth;
            if (IsPixelValue(height)) height = height / containerHeight;
            if (IsPixelValue(bottom)) bottom = bottom / containerHeight;
            if (IsPixelValue(right)) right = right / containerWidth;

            return (left, top, width, height, bottom, right);
        }

        public static (int Left, int Top, int Width, int Height) ConvertProportionalToPixels(
            int containerWidth, int containerHeight,
            double left, double top, double width, double height)
        {
            return (
                (int)(left * containerWidth),
                (int)(top * containerHeight),
                (int)(width * containerWidth),
                (int)(height * containerHeight)
            );
        }
    }
}
