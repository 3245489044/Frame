#nullable enable

using System;

namespace Modules.LayoutAnalysis
{
    public static class LayoutAnalysisConfig
    {
        public static class PropertyNames
        {
            public const string X = "X";
            public const string Y = "Y";
            public const string Left = "Left";
            public const string Top = "Top";
            public const string Width = "Width";
            public const string Height = "Height";
            public const string Location = "Location";
            public const string Size = "Size";
            public const string Id = "Id";
            public const string Name = "Name";
            public const string Text = "Text";
            public const string TypeName = "TypeName";
            public const string ParentName = "ParentName";
            public const string ParentId = "ParentId";
            public const string HasChildren = "HasChildren";
            public const string SuffixPosition = ".Position";
            public const string SuffixSize = ".Size";
            public const string SuffixWidth = ".Width";
            public const string SuffixHeight = ".Height";
            public const string SuffixText = ".Text";
        }

        public static class FormatterNames
        {
            public const string Json = "json";
            public const string Yaml = "yaml";
            public const string Xml = "xml";
            public const string Markdown = "markdown";
            public const string Text = "text";
        }

        public static class FormatTemplates
        {
            public const string JsonOpenBrace = "{";
            public const string JsonCloseBrace = "}";
            public const string JsonEntitiesArray = "  \"entities\": [";
            public const string JsonEntityOpen = "    {";
            public const string JsonEntityClose = "    }";
            public const string JsonPositionOpen = "      \"position\": {";
            public const string JsonPositionClose = "      },";
            public const string JsonSpatialArray = "  \"spatialRelations\": [";
            public const string JsonHierarchyArray = "  \"hierarchyRelations\": [";

            public const string MdTitle = "# 布局数据报告";
            public const string MdEntitySection = "## 实体列表";
            public const string MdEntityHeader = "| 实体ID | 位置 | 尺寸 | 标签 |";
            public const string MdEntitySeparator = "|--------|------|------|------|";
            public const string MdSpatialSection = "## 空间关系";
            public const string MdHierarchySection = "## 层级关系";
            public const string MdRelationItem = "- {0} {1} {2}";
            public const string MdHierarchyItem = "- {0} ({1}) 的父容器是 {2}";

            public const string TextTitle = "=== 布局数据收集报告 ===";
            public const string TextGeneratedTime = "生成时间: {0}";
            public const string TextEntityCount = "实体数量: {0}";
            public const string TextSpatialSection = "## 空间关系";
            public const string TextHierarchySection = "## 层级关系";

            public const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
            public const string XmlLayoutOpen = "<layout>";
            public const string XmlLayoutClose = "</layout>";
            public const string XmlEntitiesOpen = "  <entities>";
            public const string XmlEntitiesClose = "  </entities>";

            public const string YamlEntities = "entities:";
            public const string YamlSpatial = "spatialRelations:";
            public const string YamlHierarchy = "hierarchyRelations:";
        }

        public static class ReportTemplates
        {
            public const string TextReportTitle = "=== UI 布局报告 ===";
            public const string TextReportForm = "表单: {0} ({1}x{2})";
            public const string TextReportCount = "控件数量: {0}";
            public const string TextReportSeparator = "---";
            public const string TextReportItem = "[{0}] {1}: 位置({2},{3}) 尺寸({4}x{5}) 文本=\"{6}\"";

            public const string ValidationReportTitle = "=== 校验报告 ===";
            public const string ValidationPassed = "通过: {0}";
            public const string ValidationFailed = "失败: {0}";
            public const string ValidationFailedDetail = "失败详情:";
            public const string ValidationFailedItem = "  ✗ {0}: {1}";

            public const string ValidationSuccess = "校验通过";
            public const string ValidationMismatch = "值不匹配: 期望 '{0}', 实际 '{1}'";
        }

        public static class ValidationMessages
        {
            public const string PositionError = "位置错误: 期望({0},{1}), 实际({2},{3})";
            public const string SizeError = "尺寸错误: 期望{0}x{1}, 实际{2}x{3}";
            public const string TextError = "文本错误: 期望'{0}', 实际'{1}'";
            public const string NotFound = "控件 '{0}' 未找到";
            public const string UnsupportedProperty = "不支持的属性: {0}";
        }

        public static class RelationDescriptions
        {
            public const string Overlap = "overlap";
            public const string Spatial = "spatial";
            public const string CompleteOverlap = "完全覆盖";
            public const string SevereOverlap = "大部分重叠";
            public const string ModerateOverlap = "中度重叠";
            public const string SlightOverlap = "轻微重叠";
            public const string Contact = "接触";

            public const string TopLeft = "左上方";
            public const string BottomLeft = "左下方";
            public const string TopRight = "右上方";
            public const string BottomRight = "右下方";
            public const string Left = "左侧";
            public const string Right = "右侧";
            public const string Top = "上方";
            public const string Bottom = "下方";

            public const string Group = "group";
            public const string Proximity = "proximity";
            public const string EvidencePrefix = "命名前缀相同: {0}";
            public const string EvidenceProximity = "空间邻近";
        }

        public static class TagConstants
        {
            public const string LeftAligned = "left-aligned";
            public const string RightAligned = "right-aligned";
            public const string TopAligned = "top-aligned";
            public const string BottomAligned = "bottom-aligned";
            public const string HorizontallyCentered = "horizontally-centered";
            public const string VerticallyCentered = "vertically-centered";
            public const string Wide = "wide";
            public const string Tall = "tall";
            public const string Narrow = "narrow";
            public const string Short = "short";
            public const string Landscape = "landscape";
            public const string Portrait = "portrait";

            public const double EdgeThreshold = 0.1;
            public const double CenterThreshold = 0.1;
            public const double WideThreshold = 0.8;
            public const double TallThreshold = 0.8;
            public const double NarrowThreshold = 0.1;
            public const double ShortThreshold = 0.1;
            public const double LandscapeRatio = 2.0;
            public const double PortraitRatio = 0.5;
        }

        public static class DefaultValues
        {
            public const int DefaultWidth = 100;
            public const int DefaultHeight = 20;
            public const string DefaultEntityId = "entity";
            public const string DefaultType = "entity";
            public const string UnnamedPrefix = "unnamed_";
            public const string UnknownGroup = "unknown";
            public const string NullString = "null";
            public const string EmptyString = "";
            public const string NullType = "null";
            public const string NumericType = "numeric";
            public const string BooleanType = "boolean";
            public const string StringType = "string";
            public const string EnumType = "enum";
            public const string DateTimeType = "datetime";
            public const string ComplexType = "complex";
        }

        public static class Separators
        {
            public static readonly char[] NameSplitChars = { '_', '.', '-' };
            public const char Dot = '.';
            public const string Comma = ", ";
            public const string Pipe = "|";
        }

        public static class NumericConstants
        {
            public const int ProximityThreshold = 50;
            public const int DirectionThreshold = 5;
            public const int MaxDictionaryKeys = 10;
            public const int DefaultMaxDepth = 3;
        }

        public static class FormatStrings
        {
            public const string PercentFormat = "F0";
            public const string NumberFormat = "F{0}";
            public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
            public const string RectangleFormat = "Rectangle({0},{1},{2},{3})";
            public const string PointFormat = "Point({0},{1})";
            public const string SizeFormat = "Size({0},{1})";
            public const string DictionaryFormat = "Dictionary[{0}]";
            public const string CollectionFormat = "Collection[{0}]";
            public const string TypeNameFormat = "[{0}]";
            public const string OverlapRatioFormat = "{0} ({1:P1})";
        }
    }
}
