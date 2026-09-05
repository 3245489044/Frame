#region USINGS
using System;
using System.Collections.Generic;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Models
{
    public class CallFrameRecord
    {
        public string NodeKey { get => CallKey; set => CallKey = value; }

        public string CallKey { get; set; }

        public string Symbol { get; set; }

        public string Status { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public TimeSpan? Duration => EndTime.HasValue ? EndTime - StartTime : null;

        public object Input { get; set; }

        public object Output { get; set; }

        public DateTime Timestamp { get; set; }

        public Dictionary<string, object> Inputs { get; set; } = new();

        public Dictionary<string, object> Outputs { get; set; } = new();

        public string Error { get; set; }

        public List<CallFrameRecord> Children { get; set; } = new();
    }
}
