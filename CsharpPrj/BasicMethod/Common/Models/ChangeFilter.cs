#nullable enable

using System;

namespace Common.Models
{
    public class ChangeFilter
    {
        public string? ModuleId { get; set; }
        public OperationType? OperationType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Operator { get; set; }
    }
}
