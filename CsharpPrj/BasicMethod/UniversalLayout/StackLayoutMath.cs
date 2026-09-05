#nullable enable
using System;
using System.Collections.Generic;

namespace UniversalLayoutMath
{

    public enum StackLayoutType
    {
        VerticalStack,
        HorizontalStack,
        Grid,
        Flow
    }

    public struct PaddingConfig
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        public PaddingConfig(int all)
        {
            Left = Top = Right = Bottom = all;
        }

        public PaddingConfig(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    public class StackLayoutConfig
    {
        public StackLayoutType LayoutType { get; set; } = StackLayoutType.VerticalStack;
        public int Spacing { get; set; } = 5;
        public bool StretchWidth { get; set; } = true;
        public bool StretchHeight { get; set; } = false;
        public int Columns { get; set; } = 2;
        public PaddingConfig Padding { get; set; } = new PaddingConfig(0);
        public int TitleContentSpacing { get; set; } = 0;
        public List<string> TitleControlNames { get; set; } = new List<string>();
    }

    public struct StackLayoutItem
    {
        public int Index { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }


    public static class StackLayoutMath
    {
        public static List<StackLayoutItem> CalculateVerticalStack(
            int containerWidth, int containerHeight,
            List<(int Width, int Height)> itemSizes, StackLayoutConfig config)
        {
            int itemCount = itemSizes.Count;
            var results = new List<StackLayoutItem>(itemCount);
            if (itemCount == 0) return results;

            int spacing = config.Spacing;
            var padding = config.Padding;
            int currentY = padding.Top;
            int availableWidth = containerWidth - padding.Left - padding.Right;

            bool hasTitleContentSpacing = config.TitleContentSpacing > 0 && config.TitleControlNames.Count > 0;

            if (config.StretchWidth && config.StretchHeight)
            {
                int remainingHeight = containerHeight - padding.Top - padding.Bottom - spacing * (itemCount - 1);
                int itemHeight = Math.Max(20, remainingHeight / itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    results.Add(new StackLayoutItem
                    {
                        Index = i,
                        X = padding.Left,
                        Y = currentY,
                        Width = availableWidth,
                        Height = itemHeight
                    });
                    currentY += itemHeight + spacing;
                }
            }
            else if (config.StretchWidth)
            {
                for (int i = 0; i < itemCount; i++)
                {
                    int itemHeight = Math.Max(20, itemSizes[i].Height);
                    results.Add(new StackLayoutItem
                    {
                        Index = i,
                        X = padding.Left,
                        Y = currentY,
                        Width = availableWidth,
                        Height = itemHeight
                    });
                    currentY += itemHeight + spacing;
                }
            }
            else
            {
                for (int i = 0; i < itemCount; i++)
                {
                    int itemWidth = Math.Max(20, Math.Min(itemSizes[i].Width, availableWidth));
                    int itemHeight = Math.Max(20, itemSizes[i].Height);
                    results.Add(new StackLayoutItem
                    {
                        Index = i,
                        X = padding.Left,
                        Y = currentY,
                        Width = itemWidth,
                        Height = itemHeight
                    });
                    currentY += itemHeight + spacing;
                }
            }

            return results;
        }

        public static List<StackLayoutItem> CalculateVerticalStack(
            int containerWidth, int containerHeight,
            int itemCount, StackLayoutConfig config)
        {
            var defaultSizes = CreateDefaultSizes(itemCount, containerWidth, containerHeight, config);
            return CalculateVerticalStack(containerWidth, containerHeight, defaultSizes, config);
        }

        public static List<StackLayoutItem> CalculateHorizontalStack(
            int containerWidth, int containerHeight,
            int itemCount, StackLayoutConfig config)
        {
            var results = new List<StackLayoutItem>(itemCount);
            if (itemCount == 0) return results;

            int spacing = config.Spacing;
            var padding = config.Padding;

            int currentX = padding.Left;
            int totalWidth = containerWidth;
            int remainingWidth = totalWidth - padding.Left - padding.Right - spacing * (itemCount - 1);
            int itemWidth = Math.Max(20, remainingWidth / itemCount);
            int itemHeight = containerHeight - padding.Top - padding.Bottom;

            for (int i = 0; i < itemCount; i++)
            {
                results.Add(new StackLayoutItem
                {
                    Index = i,
                    X = currentX,
                    Y = padding.Top,
                    Width = itemWidth,
                    Height = itemHeight
                });

                currentX += itemWidth + spacing;
            }

            return results;
        }

        public static List<StackLayoutItem> CalculateGrid(
            int containerWidth, int containerHeight,
            int itemCount, StackLayoutConfig config)
        {
            var results = new List<StackLayoutItem>(itemCount);
            if (itemCount == 0) return results;

            int columns = Math.Max(1, config.Columns);
            int rows = (int)Math.Ceiling((double)itemCount / columns);
            int spacing = config.Spacing;
            var padding = config.Padding;

            int availableWidth = containerWidth - padding.Left - padding.Right - spacing * (columns - 1);
            int availableHeight = containerHeight - padding.Top - padding.Bottom - spacing * (rows - 1);

            int cellWidth = Math.Max(20, availableWidth / columns);
            int cellHeight = Math.Max(20, availableHeight / rows);

            for (int i = 0; i < itemCount; i++)
            {
                int col = i % columns;
                int row = i / columns;

                results.Add(new StackLayoutItem
                {
                    Index = i,
                    X = padding.Left + col * (cellWidth + spacing),
                    Y = padding.Top + row * (cellHeight + spacing),
                    Width = cellWidth,
                    Height = cellHeight
                });
            }

            return results;
        }

        public static List<StackLayoutItem> CalculateFlow(
            int containerWidth, int containerHeight,
            List<(int Width, int Height)> itemSizes,
            StackLayoutConfig config)
        {
            var results = new List<StackLayoutItem>(itemSizes.Count);
            if (itemSizes.Count == 0) return results;

            int spacing = config.Spacing;
            var padding = config.Padding;

            int currentX = padding.Left;
            int currentY = padding.Top;
            int rowHeight = 0;
            int maxWidth = containerWidth - padding.Right;

            for (int i = 0; i < itemSizes.Count; i++)
            {
                var (itemWidth, itemHeight) = itemSizes[i];

                if (currentX + itemWidth > maxWidth && currentX > padding.Left)
                {
                    currentX = padding.Left;
                    currentY += rowHeight + spacing;
                    rowHeight = 0;
                }

                results.Add(new StackLayoutItem
                {
                    Index = i,
                    X = currentX,
                    Y = currentY,
                    Width = itemWidth,
                    Height = itemHeight
                });

                currentX += itemWidth + spacing;
                rowHeight = Math.Max(rowHeight, itemHeight);
            }

            return results;
        }

        public static List<StackLayoutItem> Calculate(
            int containerWidth, int containerHeight,
            int itemCount, StackLayoutConfig config)
        {
            return config.LayoutType switch
            {
                StackLayoutType.VerticalStack => CalculateVerticalStack(containerWidth, containerHeight, itemCount, config),
                StackLayoutType.HorizontalStack => CalculateHorizontalStack(containerWidth, containerHeight, itemCount, config),
                StackLayoutType.Grid => CalculateGrid(containerWidth, containerHeight, itemCount, config),
                StackLayoutType.Flow => CalculateFlow(containerWidth, containerHeight,
                    CreateDefaultSizes(itemCount, containerWidth, containerHeight, config), config),
                _ => CalculateVerticalStack(containerWidth, containerHeight, itemCount, config)
            };
        }

        public static (int TotalWidth, int TotalHeight) CalculateTotalSize(
            int itemCount, StackLayoutConfig config, int defaultItemSize = 100)
        {
            int spacing = config.Spacing;
            var padding = config.Padding;

            switch (config.LayoutType)
            {
                case StackLayoutType.VerticalStack:
                    return (
                        padding.Left + padding.Right,
                        padding.Top + padding.Bottom + itemCount * defaultItemSize + (itemCount - 1) * spacing
                    );
                case StackLayoutType.HorizontalStack:
                    return (
                        padding.Left + padding.Right + itemCount * defaultItemSize + (itemCount - 1) * spacing,
                        padding.Top + padding.Bottom
                    );
                case StackLayoutType.Grid:
                    int cols = Math.Max(1, config.Columns);
                    int rows = (int)Math.Ceiling((double)itemCount / cols);
                    return (
                        padding.Left + padding.Right + cols * defaultItemSize + (cols - 1) * spacing,
                        padding.Top + padding.Bottom + rows * defaultItemSize + (rows - 1) * spacing
                    );
                default:
                    return (padding.Left + padding.Right, padding.Top + padding.Bottom + itemCount * defaultItemSize + (itemCount - 1) * spacing);
            }
        }

        private static List<(int Width, int Height)> CreateDefaultSizes(
            int count, int containerWidth, int containerHeight, StackLayoutConfig config)
        {
            var sizes = new List<(int, int)>(count);
            int defaultW = Math.Max(60, (containerWidth - config.Padding.Left - config.Padding.Right) / 2);
            int defaultH = Math.Max(30, (containerHeight - config.Padding.Top - config.Padding.Bottom) / 3);
            for (int i = 0; i < count; i++)
            {
                sizes.Add((defaultW, defaultH));
            }
            return sizes;
        }
    }
}
