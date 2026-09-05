#nullable enable

using System;
using System.Collections.Generic;

namespace Common.Models
{
    public class ChangeRecord
    {
        public Guid Id { get; set; }
        public string ModuleId { get; set; } = string.Empty;
        public OperationType Operation { get; set; }
        public Guid ReferenceId { get; set; }
        public Dictionary<string, object> BeforeState { get; set; } = new();
        public Dictionary<string, object> AfterState { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Operator { get; set; } = "system";
        public string Reason { get; set; } = string.Empty;
    }
}
