#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Modules.LayoutAnalysis.Formatting;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis
{
    public class DataOrganizer
    {
        public string GenerateNaturalLanguageDescription(List<NormalizedEntity> entities)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"共有 {entities.Count} 个元素：");

            foreach (var entity in entities.OrderBy(e => e.RelativePosition.Top).ThenBy(e => e.RelativePosition.Left))
            {
                var pos = entity.RelativePosition;
                sb.AppendLine($"- {entity.Id}：位于 ({pos.Left:P0}, {pos.Top:P0})，尺寸 ({pos.Width:P0} x {pos.Height:P0})");

                if (entity.Tags.Any())
                {
                    sb.AppendLine($"  标签：{string.Join("、", entity.Tags)}");
                }
            }

            return sb.ToString();
        }

        public string GenerateStructuredOutput(List<NormalizedEntity> entities, OutputFormat format)
        {
            return format switch
            {
                OutputFormat.Json => GenerateJson(entities),
                OutputFormat.Text => GenerateText(entities),
                OutputFormat.Markdown => GenerateMarkdown(entities),
                _ => GenerateText(entities)
            };
        }

        public string GenerateRelationDescription(List<SpatialRelation> relations,
            List<HierarchyRelation> hierarchyRelations)
        {
            var sb = new StringBuilder();

            if (relations.Any())
            {
                sb.AppendLine("空间关系：");
                foreach (var rel in relations)
                {
                    sb.AppendLine($"- {rel.From} {rel.Description} {rel.To}");
                }
            }

            if (hierarchyRelations.Any())
            {
                sb.AppendLine("层级关系：");
                foreach (var rel in hierarchyRelations)
                {
                    sb.AppendLine($"- {rel.ChildId} 的父容器是 {rel.ParentId}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// JSON 出口唯一化（LayoutAnalysis.md §4.16）：不再就地手写第三套 JSON，
        /// 统一复用 <see cref="JsonLayoutFormatter"/>（System.Text.Json + InvariantCulture + 控制字符转义）。
        /// </summary>
        private static string GenerateJson(List<NormalizedEntity> entities)
        {
            return new JsonLayoutFormatter().Format(entities);
        }

        private static string GenerateText(List<NormalizedEntity> entities)
        {
            var sb = new StringBuilder();
            foreach (var e in entities)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: ({1:F2}, {2:F2}) [{3:F2}x{4:F2}]",
                    e.Id, e.RelativePosition.Left, e.RelativePosition.Top, e.RelativePosition.Width, e.RelativePosition.Height));
            }
            return sb.ToString();
        }

        private static string GenerateMarkdown(List<NormalizedEntity> entities)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| ID | Left | Top | Width | Height |");
            sb.AppendLine("|----|------|-----|-------|--------|");
            foreach (var e in entities)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1:F2} | {2:F2} | {3:F2} | {4:F2} |",
                    e.Id, e.RelativePosition.Left, e.RelativePosition.Top, e.RelativePosition.Width, e.RelativePosition.Height));
            }
            return sb.ToString();
        }
    }
}
