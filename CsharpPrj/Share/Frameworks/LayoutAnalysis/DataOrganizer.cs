#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private static string GenerateJson(List<NormalizedEntity> entities)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                sb.AppendLine("  {");
                sb.AppendLine($"    \"id\": \"{e.Id}\",");
                sb.AppendLine($"    \"position\": {{ \"left\": {e.RelativePosition.Left}, \"top\": {e.RelativePosition.Top}, \"width\": {e.RelativePosition.Width}, \"height\": {e.RelativePosition.Height} }}");
                sb.Append("  }");
                if (i < entities.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        private static string GenerateText(List<NormalizedEntity> entities)
        {
            var sb = new StringBuilder();
            foreach (var e in entities)
            {
                sb.AppendLine($"{e.Id}: ({e.RelativePosition.Left:F2}, {e.RelativePosition.Top:F2}) [{e.RelativePosition.Width:F2}x{e.RelativePosition.Height:F2}]");
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
                sb.AppendLine($"| {e.Id} | {e.RelativePosition.Left:F2} | {e.RelativePosition.Top:F2} | {e.RelativePosition.Width:F2} | {e.RelativePosition.Height:F2} |");
            }
            return sb.ToString();
        }
    }
}
