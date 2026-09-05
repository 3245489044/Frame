#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;

namespace Modules.LayoutAnalysis.Models
{
    public struct ElementPosition
    {
        public string Id;
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public ElementPosition(string id, int x, int y, int width, int height)
        {
            Id = id;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    public struct RelativePosition
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public readonly struct NormalizedBounds
    {
        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }

        public NormalizedBounds(RelativePosition relativePos)
        {
            Left = relativePos.Left;
            Top = relativePos.Top;
            Right = relativePos.Left + relativePos.Width;
            Bottom = relativePos.Top + relativePos.Height;
        }

        public NormalizedBounds(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    public class NormalizedEntity
    {
        public string Id { get; set; } = string.Empty;
        public ElementPosition AbsolutePosition { get; set; }
        public RelativePosition RelativePosition { get; set; }
        public NormalizedBounds NormalizedBounds { get; set; }
        public Dictionary<string, NormalizedProperty> Properties { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    public class NormalizedProperty
    {
        public string Name { get; set; } = string.Empty;
        public object? RawValue { get; set; }
        public object? NormalizedValue { get; set; }
        public string ValueType { get; set; } = string.Empty;
    }

    public class FlatEntity
    {
        public Dictionary<string, object?> Fields { get; set; } = new();
    }

    public class SpatialRelation
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class HierarchyRelation
    {
        public string ChildId { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string ChildType { get; set; } = string.Empty;
    }

    public class SemanticRelation
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string RelationType { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
    }

    public class ElementSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = string.Empty;
        public ElementPosition Position { get; set; }
        public Dictionary<string, object?> Properties { get; set; } = new();
    }

    public class TypeInfoSnapshot
    {
        public string TypeName { get; set; } = string.Empty;
        public string? BaseTypeName { get; set; }
        public int FieldCount { get; set; }
        public int PropertyCount { get; set; }
        public int MethodCount { get; set; }
        public bool IsValueType { get; set; }
        public bool IsEnum { get; set; }
        public bool IsInterface { get; set; }
    }

    public class ObjectSnapshot
    {
        public string TypeName { get; set; } = string.Empty;
        public string? Identifier { get; set; }
        public Dictionary<string, object?> Fields { get; set; } = new();
        public Dictionary<string, object?> Properties { get; set; } = new();
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    }

    public class LayoutCollectionResult
    {
        public List<ElementPosition> RawEntities { get; set; } = new();
        public List<NormalizedEntity> NormalizedEntities { get; set; } = new();
        public List<SpatialRelation> SpatialRelations { get; set; } = new();
        public List<HierarchyRelation> HierarchyRelations { get; set; } = new();
        public List<SemanticRelation> SemanticRelations { get; set; } = new();

        public LayoutGraphContext? GraphContext { get; set; }

        public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    }

    public class LayoutData
    {
        public List<NormalizedEntity> Entities { get; set; } = new();
        public List<SpatialRelation> SpatialRelations { get; set; } = new();
        public List<HierarchyRelation> HierarchyRelations { get; set; } = new();
    }

    public enum OutputFormat
    {
        Json,
        Yaml,
        Xml,
        Markdown,
        Text
    }

    public class OutputOptions
    {
        public OutputFormat Format { get; set; } = OutputFormat.Json;
        public bool IncludeProperties { get; set; } = true;
        public bool IncludeTags { get; set; } = true;
        public bool IncludeRelations { get; set; } = true;
    }
}
