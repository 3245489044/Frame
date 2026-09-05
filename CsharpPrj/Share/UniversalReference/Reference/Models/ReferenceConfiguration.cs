#nullable enable

using System.Collections.Generic;

namespace UniversalWorkflowFramework.Reference.Models
{
    public class ReferenceConfiguration
    {
        public string Name { get; set; } = "default";
        public string Description { get; set; } = "默认配置";
        public ReferenceSchema Schema { get; set; } = new();
        public StorageConfiguration Storage { get; set; } = new();
        public CacheConfiguration Cache { get; set; } = new();
        public ValidationConfiguration Validation { get; set; } = new();
    }

    public class ReferenceSchema
    {
        public List<NodeType> NodeTypes { get; set; } = new();
        public List<EdgeType> EdgeTypes { get; set; } = new();
        public List<FilterRule> Filters { get; set; } = new();
    }

    public class NodeType
    {
        public string Type { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Color { get; set; } = "";
        public bool Visible { get; set; } = true;
        public int SortWeight { get; set; }
        public List<string> DisplayProperties { get; set; } = new();
    }

    public class EdgeType
    {
        public string Type { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Color { get; set; } = "";
        public string LineStyle { get; set; } = "solid";
        public bool Visible { get; set; } = true;
    }

    public class StorageConfiguration
    {
        public string Type { get; set; } = "inmemory";
        public string ConnectionString { get; set; } = "";
        public int Timeout { get; set; } = 30000;
        public int BatchSize { get; set; } = 1000;
    }

    public class CacheConfiguration
    {
        public bool Enabled { get; set; } = true;
        public int ExpirationMinutes { get; set; } = 5;
        public int MaxSize { get; set; } = 1000;
    }

    public class ValidationConfiguration
    {
        public bool Enabled { get; set; } = true;
        public string Level { get; set; } = "Module";
        public List<ValidationRule> Rules { get; set; } = new();
    }

    public class ValidationRule
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class FilterRule
    {
        public FilterType Type { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}