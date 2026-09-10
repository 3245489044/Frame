#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis.Formatting
{
    public interface ILayoutFormatter
    {
        string Name { get; }
        string Format(List<NormalizedEntity> entities);
        string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations);
    }

    public abstract class FormatterBase : ILayoutFormatter
    {
        public abstract string Name { get; }
        public abstract string Format(List<NormalizedEntity> entities);
        public abstract string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations);

        protected static StringBuilder CreateBuilder()
        {
            return new StringBuilder(4096);
        }

        protected static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length * 2);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        protected static string FormatPercent(double value)
        {
            return (value * 100).ToString(LayoutAnalysisConfig.FormatStrings.PercentFormat) + "%";
        }

        protected static string FormatDateTime(DateTime dateTime)
        {
            return dateTime.ToString(LayoutAnalysisConfig.FormatStrings.DateTimeFormat);
        }
    }

    internal static class XmlEscapeHelper
    {
        private static readonly string Amp = "&" + "amp;";
        private static readonly string Lt = "&" + "lt;";
        private static readonly string Gt = "&" + "gt;";
        private static readonly string Quot = "&" + "quot;";
        private static readonly string Apos = "&" + "apos;";

        public static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length * 2);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': sb.Append(Amp); break;
                    case '<': sb.Append(Lt); break;
                    case '>': sb.Append(Gt); break;
                    case '"': sb.Append(Quot); break;
                    case '\'': sb.Append(Apos); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    public class JsonLayoutFormatter : FormatterBase, ILayoutFormatter
    {
        public override string Name => LayoutAnalysisConfig.FormatterNames.Json;

        public override string Format(List<NormalizedEntity> entities)
        {
            return FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var sb = CreateBuilder();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonOpenBrace);
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonEntitiesArray);

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonEntityOpen);
                sb.AppendLine("      \"id\": \"" + EscapeJson(entity.Id) + "\",");
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonPositionOpen);
                sb.AppendLine("        \"left\": " + entity.RelativePosition.Left + ",");
                sb.AppendLine("        \"top\": " + entity.RelativePosition.Top + ",");
                sb.AppendLine("        \"width\": " + entity.RelativePosition.Width + ",");
                sb.AppendLine("        \"height\": " + entity.RelativePosition.Height);
                sb.AppendLine("      },");
                sb.AppendLine("      \"tags\": [" + string.Join(", ", entity.Tags.Select(t => "\"" + EscapeJson(t) + "\"")) + "]");
                sb.Append("    }");
                if (i < entities.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ],");
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonSpatialArray);

            for (int i = 0; i < spatialRelations.Count; i++)
            {
                var rel = spatialRelations[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"from\": \"" + EscapeJson(rel.From) + "\",");
                sb.AppendLine("      \"to\": \"" + EscapeJson(rel.To) + "\",");
                sb.AppendLine("      \"type\": \"" + EscapeJson(rel.Type) + "\",");
                sb.AppendLine("      \"description\": \"" + EscapeJson(rel.Description) + "\"");
                sb.Append("    }");
                if (i < spatialRelations.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ],");
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonHierarchyArray);

            for (int i = 0; i < hierarchyRelations.Count; i++)
            {
                var rel = hierarchyRelations[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"childId\": \"" + EscapeJson(rel.ChildId) + "\",");
                sb.AppendLine("      \"parentId\": \"" + EscapeJson(rel.ParentId) + "\",");
                sb.AppendLine("      \"childType\": \"" + EscapeJson(rel.ChildType) + "\"");
                sb.Append("    }");
                if (i < hierarchyRelations.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.JsonCloseBrace);

            return sb.ToString();
        }
    }

    public class MarkdownLayoutFormatter : FormatterBase, ILayoutFormatter
    {
        public override string Name => LayoutAnalysisConfig.FormatterNames.Markdown;

        public override string Format(List<NormalizedEntity> entities)
        {
            return FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var sb = CreateBuilder();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdTitle);
            sb.AppendLine();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdEntitySection);
            sb.AppendLine();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdEntityHeader);
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdEntitySeparator);

            foreach (var entity in entities)
            {
                var pos = entity.RelativePosition;
                var positionDesc = "L:" + FormatPercent(pos.Left) + " T:" + FormatPercent(pos.Top);
                var sizeDesc = "W:" + FormatPercent(pos.Width) + " H:" + FormatPercent(pos.Height);
                var tags = string.Join(", ", entity.Tags);

                sb.AppendLine("| " + entity.Id + " | " + positionDesc + " | " + sizeDesc + " | " + tags + " |");
            }

            if (spatialRelations.Any())
            {
                sb.AppendLine();
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdSpatialSection);
                foreach (var rel in spatialRelations)
                {
                    sb.AppendLine("- " + rel.From + " " + rel.Description + " " + rel.To);
                }
            }

            if (hierarchyRelations.Any())
            {
                sb.AppendLine();
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.MdHierarchySection);
                foreach (var rel in hierarchyRelations)
                {
                    sb.AppendLine("- " + rel.ChildId + " (" + rel.ChildType + ") 的父容器是 " + rel.ParentId);
                }
            }

            return sb.ToString();
        }
    }

    public class TextLayoutFormatter : FormatterBase, ILayoutFormatter
    {
        public override string Name => LayoutAnalysisConfig.FormatterNames.Text;

        public override string Format(List<NormalizedEntity> entities)
        {
            return FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var sb = CreateBuilder();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.TextTitle);
            sb.AppendLine(string.Format(LayoutAnalysisConfig.FormatTemplates.TextGeneratedTime, FormatDateTime(DateTime.Now)));
            sb.AppendLine(string.Format(LayoutAnalysisConfig.FormatTemplates.TextEntityCount, entities.Count));
            sb.AppendLine();

            var sorted = entities.OrderBy(e => e.RelativePosition.Top)
                                .ThenBy(e => e.RelativePosition.Left);

            foreach (var entity in sorted)
            {
                sb.AppendLine(DescribeEntity(entity));
            }

            if (spatialRelations.Any())
            {
                sb.AppendLine();
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.TextSpatialSection);
                foreach (var rel in spatialRelations)
                {
                    sb.AppendLine("- " + rel.From + " " + rel.Description + " " + rel.To);
                }
            }

            if (hierarchyRelations.Any())
            {
                sb.AppendLine();
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.TextHierarchySection);
                foreach (var rel in hierarchyRelations)
                {
                    sb.AppendLine("- " + rel.ChildId + " (" + rel.ChildType + ") 的父容器是 " + rel.ParentId);
                }
            }

            return sb.ToString();
        }

        private string DescribeEntity(NormalizedEntity entity)
        {
            var sb = CreateBuilder();
            sb.Append("实体 '" + entity.Id + "'");

            var pos = entity.RelativePosition;
            sb.Append(" 位于区域: 左侧" + FormatPercent(pos.Left) + " ~ " + FormatPercent(pos.Left + pos.Width) + ", " +
                      "顶部" + FormatPercent(pos.Top) + " ~ " + FormatPercent(pos.Top + pos.Height));

            sb.Append("，尺寸占比: 宽度" + FormatPercent(pos.Width) + "，高度" + FormatPercent(pos.Height));

            if (entity.Tags.Any())
            {
                sb.Append("，标签: " + string.Join(", ", entity.Tags));
            }

            return sb.ToString();
        }
    }

    public class LayoutFormatterManager
    {
        private readonly Dictionary<string, ILayoutFormatter> _formatterCache;

        public LayoutFormatterManager()
        {
            _formatterCache = new Dictionary<string, ILayoutFormatter>(StringComparer.OrdinalIgnoreCase);
            RegisterDefaultFormatters();
        }

        private void RegisterDefaultFormatters()
        {
            var jsonFormatter = new JsonLayoutFormatter();
            var yamlFormatter = new YamlLayoutFormatter();
            var xmlFormatter = new XmlLayoutFormatter();
            var markdownFormatter = new MarkdownLayoutFormatter();
            var textFormatter = new TextLayoutFormatter();

            _formatterCache[LayoutAnalysisConfig.FormatterNames.Json] = jsonFormatter;
            _formatterCache[LayoutAnalysisConfig.FormatterNames.Yaml] = yamlFormatter;
            _formatterCache[LayoutAnalysisConfig.FormatterNames.Xml] = xmlFormatter;
            _formatterCache[LayoutAnalysisConfig.FormatterNames.Markdown] = markdownFormatter;
            _formatterCache[LayoutAnalysisConfig.FormatterNames.Text] = textFormatter;
        }

        public ILayoutFormatter GetLayoutFormatter(string format)
        {
            if (_formatterCache.TryGetValue(format, out var formatter))
                return formatter;

            throw new NotSupportedException("Format '" + format + "' is not supported. Available formats: " + string.Join(", ", _formatterCache.Keys));
        }

        public string FormatEntities(List<NormalizedEntity> entities, string format)
        {
            var formatter = GetLayoutFormatter(format);
            return formatter.FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations, string format)
        {
            var formatter = GetLayoutFormatter(format);
            return formatter.FormatWithRelations(entities, spatialRelations, hierarchyRelations);
        }

        public IEnumerable<string> SupportedFormats => _formatterCache.Keys;
    }

    public class YamlLayoutFormatter : FormatterBase, ILayoutFormatter
    {
        public override string Name => LayoutAnalysisConfig.FormatterNames.Yaml;

        public override string Format(List<NormalizedEntity> entities)
        {
            return FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var sb = CreateBuilder();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.YamlEntities);

            foreach (var entity in entities)
            {
                sb.AppendLine("  - id: \"" + EscapeJson(entity.Id) + "\"");
                sb.AppendLine("    position:");
                sb.AppendLine("      left: " + entity.RelativePosition.Left);
                sb.AppendLine("      top: " + entity.RelativePosition.Top);
                sb.AppendLine("      width: " + entity.RelativePosition.Width);
                sb.AppendLine("      height: " + entity.RelativePosition.Height);
                if (entity.Tags.Any())
                {
                    sb.AppendLine("    tags: [" + string.Join(", ", entity.Tags.Select(t => "\"" + EscapeJson(t) + "\"")) + "]");
                }
            }

            if (spatialRelations.Any())
            {
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.YamlSpatial);
                foreach (var rel in spatialRelations)
                {
                    sb.AppendLine("  - from: \"" + EscapeJson(rel.From) + "\"");
                    sb.AppendLine("    to: \"" + EscapeJson(rel.To) + "\"");
                    sb.AppendLine("    type: \"" + EscapeJson(rel.Type) + "\"");
                    sb.AppendLine("    description: \"" + EscapeJson(rel.Description) + "\"");
                }
            }

            if (hierarchyRelations.Any())
            {
                sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.YamlHierarchy);
                foreach (var rel in hierarchyRelations)
                {
                    sb.AppendLine("  - childId: \"" + EscapeJson(rel.ChildId) + "\"");
                    sb.AppendLine("    parentId: \"" + EscapeJson(rel.ParentId) + "\"");
                    sb.AppendLine("    childType: \"" + EscapeJson(rel.ChildType) + "\"");
                }
            }

            return sb.ToString();
        }
    }

    public class XmlLayoutFormatter : FormatterBase, ILayoutFormatter
    {
        public override string Name => LayoutAnalysisConfig.FormatterNames.Xml;

        public override string Format(List<NormalizedEntity> entities)
        {
            return FormatWithRelations(entities, new List<SpatialRelation>(), new List<HierarchyRelation>());
        }

        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var sb = CreateBuilder();
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.XmlDeclaration);
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.XmlLayoutOpen);
            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.XmlEntitiesOpen);

            foreach (var entity in entities)
            {
                sb.AppendLine("    <entity id=\"" + XmlEscapeHelper.EscapeXml(entity.Id) + "\">");
                sb.AppendLine("      <position>");
                sb.AppendLine("        <left>" + entity.RelativePosition.Left + "</left>");
                sb.AppendLine("        <top>" + entity.RelativePosition.Top + "</top>");
                sb.AppendLine("        <width>" + entity.RelativePosition.Width + "</width>");
                sb.AppendLine("        <height>" + entity.RelativePosition.Height + "</height>");
                sb.AppendLine("      </position>");
                if (entity.Tags.Any())
                {
                    sb.AppendLine("      <tags>" + string.Join(", ", entity.Tags.Select(t => "<tag>" + XmlEscapeHelper.EscapeXml(t) + "</tag>")) + "</tags>");
                }
                sb.AppendLine("    </entity>");
            }

            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.XmlEntitiesClose);

            if (spatialRelations.Any())
            {
                sb.AppendLine("  <spatialRelations>");
                foreach (var rel in spatialRelations)
                {
                    sb.AppendLine("    <relation from=\"" + XmlEscapeHelper.EscapeXml(rel.From) + "\" to=\"" + XmlEscapeHelper.EscapeXml(rel.To) + "\" type=\"" + XmlEscapeHelper.EscapeXml(rel.Type) + "\">");
                    sb.AppendLine("      <description>" + XmlEscapeHelper.EscapeXml(rel.Description) + "</description>");
                    sb.AppendLine("    </relation>");
                }
                sb.AppendLine("  </spatialRelations>");
            }

            if (hierarchyRelations.Any())
            {
                sb.AppendLine("  <hierarchyRelations>");
                foreach (var rel in hierarchyRelations)
                {
                    sb.AppendLine("    <relation childId=\"" + XmlEscapeHelper.EscapeXml(rel.ChildId) + "\" parentId=\"" + XmlEscapeHelper.EscapeXml(rel.ParentId) + "\" childType=\"" + XmlEscapeHelper.EscapeXml(rel.ChildType) + "\" />");
                }
                sb.AppendLine("  </hierarchyRelations>");
            }

            sb.AppendLine(LayoutAnalysisConfig.FormatTemplates.XmlLayoutClose);
            return sb.ToString();
        }
    }
}
