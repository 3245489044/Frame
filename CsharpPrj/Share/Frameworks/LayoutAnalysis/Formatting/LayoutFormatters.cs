#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        // 其余控制字符（U+0000–U+001F）必须转义为 \uXXXX，否则产出非法 JSON（§4.18）。
                        if (c < '\u0020')
                            sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>数值统一 InvariantCulture（§4.17：禁止按区域文化输出 "1,5" 这类数字）。</summary>
        protected static string FormatNumber(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        protected static string FormatPercent(double value)
        {
            return (value * 100).ToString(LayoutAnalysisConfig.FormatStrings.PercentFormat, CultureInfo.InvariantCulture) + "%";
        }

        protected static string FormatDateTime(DateTime dateTime)
        {
            return dateTime.ToString(LayoutAnalysisConfig.FormatStrings.DateTimeFormat, CultureInfo.InvariantCulture);
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

        /// <summary>仅实体列表：保持历史顶层数组形状（避免改变既有 JSON 消费方的解析契约）。</summary>
        public override string Format(List<NormalizedEntity> entities)
            => JsonSerializer.Serialize((entities ?? new List<NormalizedEntity>()).Select(ToJsonEntity).ToList(), JsonOptions);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // 中文等非 ASCII 保持原样；控制字符仍由 STJ 转义为 \uXXXX（§4.18）。
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 全仓唯一 JSON 序列化实现（System.Text.Json）：数字由 STJ 以 InvariantCulture 输出，
        /// 控制字符自动转义。DataOrganizer.GenerateJson 直接复用本实现（§4.16 三套 JSON 收敛为一套）。
        /// </summary>
        public override string FormatWithRelations(List<NormalizedEntity> entities, List<SpatialRelation> spatialRelations, List<HierarchyRelation> hierarchyRelations)
        {
            var payload = new Dictionary<string, object?>
            {
                ["entities"] = entities.Select(ToJsonEntity).ToList(),
                ["spatialRelations"] = spatialRelations.Select(r => new
                {
                    from = r.From,
                    to = r.To,
                    type = r.Type,
                    description = r.Description
                }).ToList(),
                ["hierarchyRelations"] = hierarchyRelations.Select(r => new
                {
                    childId = r.ChildId,
                    parentId = r.ParentId,
                    childType = r.ChildType
                }).ToList()
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static Dictionary<string, object?> ToJsonEntity(NormalizedEntity e) => new()
        {
            ["id"] = e.Id,
            ["position"] = new Dictionary<string, object?>
            {
                ["left"] = e.RelativePosition.Left,
                ["top"] = e.RelativePosition.Top,
                ["width"] = e.RelativePosition.Width,
                ["height"] = e.RelativePosition.Height
            },
            ["tags"] = e.Tags
        };
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
                sb.AppendLine("      left: " + FormatNumber(entity.RelativePosition.Left));
                sb.AppendLine("      top: " + FormatNumber(entity.RelativePosition.Top));
                sb.AppendLine("      width: " + FormatNumber(entity.RelativePosition.Width));
                sb.AppendLine("      height: " + FormatNumber(entity.RelativePosition.Height));
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
                sb.AppendLine("        <left>" + FormatNumber(entity.RelativePosition.Left) + "</left>");
                sb.AppendLine("        <top>" + FormatNumber(entity.RelativePosition.Top) + "</top>");
                sb.AppendLine("        <width>" + FormatNumber(entity.RelativePosition.Width) + "</width>");
                sb.AppendLine("        <height>" + FormatNumber(entity.RelativePosition.Height) + "</height>");
                sb.AppendLine("      </position>");
                if (entity.Tags.Any())
                {
                    // 标签以元素序列表达，不用逗号拼接（§4.20：原 ", " 会让 tag 文本与分隔符无法区分）。
                    sb.AppendLine("      <tags>" + string.Concat(entity.Tags.Select(t => "<tag>" + XmlEscapeHelper.EscapeXml(t) + "</tag>")) + "</tags>");
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
