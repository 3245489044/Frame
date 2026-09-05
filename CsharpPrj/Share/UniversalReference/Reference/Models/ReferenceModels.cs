#nullable enable

using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.Reference.Models
{
    public class Reference
    {
        public string? SourceId { get; set; }
        public string? SourceType { get; set; }
        public string? TargetId { get; set; }
        public string? TargetType { get; set; }
        public string? RelationshipType { get; set; }
        public string? Description { get; set; }
        public double Strength { get; set; } = 1.0;
    }

    public class ResourceReference
    {
        public Core.Models.ResourceIdentifier Source { get; set; } = new();
        public Core.Models.ResourceIdentifier Target { get; set; } = new();
        public ReferenceType Type { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public double Strength { get; set; } = 1.0;
    }

    public class ResourceData
    {
        public Core.Models.ResourceIdentifier Identifier { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
    }
}

