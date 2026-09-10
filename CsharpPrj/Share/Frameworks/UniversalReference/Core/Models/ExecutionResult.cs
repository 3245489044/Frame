using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.Core.Models
{
    public class ExecutionResult
    {
        public ExecutionStatus Status { get; set; }

        public Dictionary<string, object> Output { get; set; }

        public string? Error { get; set; }

        public TimeSpan ExecutionTime { get; set; }

        public ExecutionResult()
        {
            Output = new Dictionary<string, object>();
        }
    }

    public enum ExecutionStatus
    {
        Success,

        Failed,

        Timeout,

        Cancelled
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }

        public string Message { get; set; }

        public Dictionary<string, object>? Details { get; set; }

        public ValidationResult()
        {
            Message = string.Empty;
        }
    }
}