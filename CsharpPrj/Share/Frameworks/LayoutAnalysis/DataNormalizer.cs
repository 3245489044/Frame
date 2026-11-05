#nullable enable

using System.Collections.Generic;
using System.Drawing;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis
{
    public class DataNormalizer
    {
        public NormalizedEntity NormalizePosition(ElementPosition element, Rectangle referenceBounds)
        {
            // 参考系原点必须参与换算（LayoutAnalysis.md §4.6，原 :15-18 直接 X/Width 丢失 referenceBounds.X/Y）。
            // Left/Top/Width/Height 统一定义为"相对参考框原点的分数"，因此 Left/Top 减原点、宽高除以同一参考框尺寸。
            var offsetX = element.X - referenceBounds.X;
            var offsetY = element.Y - referenceBounds.Y;
            var relative = new RelativePosition
            {
                Left = referenceBounds.Width > 0 ? (double)offsetX / referenceBounds.Width : 0,
                Top = referenceBounds.Height > 0 ? (double)offsetY / referenceBounds.Height : 0,
                Width = referenceBounds.Width > 0 ? (double)element.Width / referenceBounds.Width : 0,
                Height = referenceBounds.Height > 0 ? (double)element.Height / referenceBounds.Height : 0
            };

            return new NormalizedEntity
            {
                Id = element.Id,
                AbsolutePosition = element,
                RelativePosition = relative,
                NormalizedBounds = new NormalizedBounds(relative)
            };
        }

        public List<NormalizedEntity> NormalizePositions(List<ElementPosition> elements, Rectangle referenceBounds)
        {
            var result = new List<NormalizedEntity>(elements.Count);
            foreach (var element in elements)
            {
                result.Add(NormalizePosition(element, referenceBounds));
            }
            return result;
        }

        public Dictionary<string, NormalizedProperty> NormalizeProperties(Dictionary<string, object?> rawProperties)
        {
            var result = new Dictionary<string, NormalizedProperty>();
            foreach (var kvp in rawProperties)
            {
                result[kvp.Key] = NormalizeProperty(kvp.Key, kvp.Value);
            }
            return result;
        }

        private NormalizedProperty NormalizeProperty(string name, object? value)
        {
            var prop = new NormalizedProperty
            {
                Name = name,
                RawValue = value,
                NormalizedValue = value?.ToString() ?? LayoutAnalysisConfig.DefaultValues.NullString,
                ValueType = value?.GetType().Name ?? LayoutAnalysisConfig.DefaultValues.NullType
            };
            return prop;
        }

        public List<string> GenerateSemanticTags(NormalizedEntity entity)
        {
            var tags = new List<string>();
            var pos = entity.RelativePosition;

            if (pos.Left < LayoutAnalysisConfig.TagConstants.EdgeThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.LeftAligned);
            if (pos.Left + pos.Width > 1.0 - LayoutAnalysisConfig.TagConstants.EdgeThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.RightAligned);

            if (pos.Top < LayoutAnalysisConfig.TagConstants.EdgeThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.TopAligned);
            if (pos.Top + pos.Height > 1.0 - LayoutAnalysisConfig.TagConstants.EdgeThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.BottomAligned);

            if (System.Math.Abs(pos.Left + pos.Width / 2 - 0.5) < LayoutAnalysisConfig.TagConstants.CenterThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.HorizontallyCentered);

            if (System.Math.Abs(pos.Top + pos.Height / 2 - 0.5) < LayoutAnalysisConfig.TagConstants.CenterThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.VerticallyCentered);

            if (pos.Width > LayoutAnalysisConfig.TagConstants.WideThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.Wide);
            else if (pos.Width < LayoutAnalysisConfig.TagConstants.NarrowThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.Narrow);

            if (pos.Height > LayoutAnalysisConfig.TagConstants.TallThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.Tall);
            else if (pos.Height < LayoutAnalysisConfig.TagConstants.ShortThreshold)
                tags.Add(LayoutAnalysisConfig.TagConstants.Short);

            // 长宽比必须用物理像素尺寸（同一分母），不能拿"除以参考宽/参考高"的两个分数相除：
            // 参考系非方形时两份分数分母不同，会系统性误判 landscape/portrait（LayoutAnalysis.md §4.7，原 :93-99）。
            var pixelWidth = entity.AbsolutePosition.Width;
            var pixelHeight = entity.AbsolutePosition.Height;
            if (pixelWidth > 0 && pixelHeight > 0)
            {
                var ratio = (double)pixelWidth / pixelHeight;
                if (ratio > LayoutAnalysisConfig.TagConstants.LandscapeRatio)
                    tags.Add(LayoutAnalysisConfig.TagConstants.Landscape);
                else if (ratio < LayoutAnalysisConfig.TagConstants.PortraitRatio)
                    tags.Add(LayoutAnalysisConfig.TagConstants.Portrait);
            }

            return tags;
        }
    }
}
