#nullable enable

using System;

namespace DataManage.Abstractions.Models
{
    public class GraphIndexInfo
    {
        public string ResourceType { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastRebuiltAt { get; set; }
        public long EntryCount { get; set; }
    }
}
