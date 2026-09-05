#nullable enable

using System;

namespace DataManage.Abstractions.Models
{
    public enum TransactionOperationType
    {
        Store,
        Delete,
        Update
    }

    public class TransactionOperation
    {
        public TransactionOperationType OperationType { get; set; }

        public string ResourceType { get; set; } = string.Empty;

        public string ResourceId { get; set; } = string.Empty;

        public System.Collections.Generic.Dictionary<string, object>? Metadata { get; set; }

        public string? Symbol { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}