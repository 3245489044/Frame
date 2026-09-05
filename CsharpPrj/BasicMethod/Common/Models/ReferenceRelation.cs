#nullable enable

using System;

namespace Common.Models
{
    public class ReferenceRelation
    {
        public Guid Id { get; set; }
        public string SourceModuleId { get; set; } = string.Empty;
        public string TargetModuleId { get; set; } = string.Empty;
        public RelationType RelationType { get; set; }
        public double Strength { get; set; } = 1.0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
