#nullable enable

using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.Core.Models
{
    public class Resource
    {
        public ResourceIdentifier Identifier { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public string? Symbol { get; set; }
    }
}
