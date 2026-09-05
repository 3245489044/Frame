#nullable enable
using System;

namespace UniversalLayoutMath
{
    public static class ClampMath
    {
        public static int Clamp(int value, int min, int max)
        {
            if (min > max)
            {
                int temp = min;
                min = max;
                max = temp;
            }
            return Math.Max(min, Math.Min(max, value));
        }

        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
            {
                double temp = min;
                min = max;
                max = temp;
            }
            return Math.Max(min, Math.Min(max, value));
        }

        public static int ClampSize(int size, int minSize = 1)
        {
            return Math.Max(minSize, size);
        }

        public static int ClampPosition(int position, int containerSize, int elementSize)
        {
            return Math.Max(0, Math.Min(position, containerSize - elementSize));
        }

        public static (int X, int Y, int Width, int Height) ClampRect(
            int x, int y, int width, int height,
            int containerWidth, int containerHeight,
            int minSize = 1)
        {
            width = ClampSize(width, minSize);
            height = ClampSize(height, minSize);

            x = ClampPosition(x, containerWidth, width);
            y = ClampPosition(y, containerHeight, height);

            return (x, y, width, height);
        }

        public static (int Width, int Height) ClampAspectRatio(
            int width, int height,
            double minAspectRatio, double maxAspectRatio)
        {
            if (height <= 0) return (width, height);

            double currentRatio = (double)width / height;

            if (currentRatio < minAspectRatio)
            {
                width = (int)Math.Round(height * minAspectRatio);
            }
            else if (currentRatio > maxAspectRatio)
            {
                width = (int)Math.Round(height * maxAspectRatio);
            }

            return (width, height);
        }

        public static double Normalize(double value, double min, double max)
        {
            if (Math.Abs(max - min) < double.Epsilon) return 0;
            return Clamp((value - min) / (max - min), 0, 1);
        }

        public static double MapRange(double value, double fromMin, double fromMax, double toMin, double toMax)
        {
            double normalized = (value - fromMin) / (fromMax - fromMin);
            return toMin + normalized * (toMax - toMin);
        }
    }
}
