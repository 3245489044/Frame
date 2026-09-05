#nullable enable

using System;

namespace Common.Models
{
    public class OperationResult
    {
        public bool Success { get; set; }
        public Exception? Error { get; set; }
    }
}
