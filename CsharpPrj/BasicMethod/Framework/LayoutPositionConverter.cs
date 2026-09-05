
#nullable enable

namespace Modules.Framework
{
    public struct LayoutPosition
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public HorizontalAlignment HorizontalAlignment { get; set; }

        public VerticalAlignment VerticalAlignment { get; set; }

        public static LayoutPosition FromLeftTop(double left, double top, double width, double height)
            => new() { Left = left, Top = top, Width = width, Height = height };

        public static LayoutPosition FromRightBottom(double right, double bottom, double width, double height)
            => new() { Right = right, Bottom = bottom, Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    }

    public enum HorizontalAlignment
    {
        Left,
        Center,
        Right,
        Stretch
    }

    public enum VerticalAlignment
    {
        Top,
        Center,
        Bottom,
        Stretch
    }

    public struct LayoutRect
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public static LayoutRect FromXYWH(double x, double y, double width, double height)
            => new() { X = x, Y = y, Width = width, Height = height };

        public (int X, int Y, int Width, int Height) ToInt32()
            => ((int)X, (int)Y, (int)Width, (int)Height);
    }

    public class LayoutPositionConverter
    {
        public LayoutRect Convert(LayoutPosition position, double containerWidth, double containerHeight)
        {
            double x, y, width, height;

            if (position.Width > 0)
            {
                width = position.Width;
            }
            else if (position.Width < 0)
            {
                width = Math.Abs(position.Width) * containerWidth;
            }
            else
            {
                width = containerWidth - position.Left - Math.Abs(position.Right);
            }

            if (position.Height > 0)
            {
                height = position.Height;
            }
            else if (position.Height < 0)
            {
                height = Math.Abs(position.Height) * containerHeight;
            }
            else
            {
                height = containerHeight - position.Top - Math.Abs(position.Bottom);
            }

            if (position.Right < 0)
            {
                x = containerWidth - Math.Abs(position.Right) - width;
            }
            else if (position.Right > 0 && position.Left == 0)
            {
                x = containerWidth - position.Right - width;
            }
            else
            {
                x = position.Left;
            }

            if (position.Bottom < 0)
            {
                y = containerHeight - Math.Abs(position.Bottom) - height;
            }
            else if (position.Bottom > 0 && position.Top == 0)
            {
                y = containerHeight - position.Bottom - height;
            }
            else
            {
                y = position.Top;
            }

            ApplyAlignment(ref x, ref y, ref width, ref height, position, containerWidth, containerHeight);

            return new LayoutRect
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
        }

        public LayoutRect ConvertFromBottomRight(
            double bottom, double right,
            double containerWidth, double containerHeight,
            double elementWidth, double elementHeight)
        {
            return Convert(new LayoutPosition
            {
                Bottom = bottom,
                Right = right,
                Width = elementWidth,
                Height = elementHeight,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            }, containerWidth, containerHeight);
        }

        public LayoutRect ConvertFromPercent(
            double leftPercent, double topPercent,
            double widthPercent, double heightPercent,
            double containerWidth, double containerHeight)
        {
            return new LayoutRect
            {
                X = leftPercent * containerWidth,
                Y = topPercent * containerHeight,
                Width = widthPercent * containerWidth,
                Height = heightPercent * containerHeight
            };
        }

        public LayoutRect CenterInContainer(
            double elementWidth, double elementHeight,
            double containerWidth, double containerHeight)
        {
            return new LayoutRect
            {
                X = (containerWidth - elementWidth) / 2,
                Y = (containerHeight - elementHeight) / 2,
                Width = elementWidth,
                Height = elementHeight
            };
        }

        private static void ApplyAlignment(
            ref double x, ref double y,
            ref double width, ref double height,
            LayoutPosition position,
            double containerWidth, double containerHeight)
        {
            switch (position.HorizontalAlignment)
            {
                case HorizontalAlignment.Center:
                    x = (containerWidth - width) / 2;
                    break;
                case HorizontalAlignment.Right:
                    x = containerWidth - width - Math.Abs(position.Right);
                    break;
                case HorizontalAlignment.Stretch:
                    width = containerWidth - x - Math.Abs(position.Right);
                    break;
            }

            switch (position.VerticalAlignment)
            {
                case VerticalAlignment.Center:
                    y = (containerHeight - height) / 2;
                    break;
                case VerticalAlignment.Bottom:
                    y = containerHeight - height - Math.Abs(position.Bottom);
                    break;
                case VerticalAlignment.Stretch:
                    height = containerHeight - y - Math.Abs(position.Bottom);
                    break;
            }
        }
    }
}
