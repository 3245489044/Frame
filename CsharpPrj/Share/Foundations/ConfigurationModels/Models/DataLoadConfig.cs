#nullable enable
using System.Text.Json.Serialization;

namespace Modules.ConfigurationModels
{
    public class DataLoadConfig
    {
        public string GraphQuery { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public string SqlQuery { get; set; } = string.Empty;
        public string IdField { get; set; } = "Id";
        public Dictionary<string, string> FieldMappings { get; set; } = [];
        public List<ReferenceQuery> References { get; set; } = [];
    }

    public class ReferenceQuery
    {
        public string RelationshipType { get; set; } = "References";
        public string TargetResourceType { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public List<ReferenceQuery> NestedReferences { get; set; } = [];
    }
}
