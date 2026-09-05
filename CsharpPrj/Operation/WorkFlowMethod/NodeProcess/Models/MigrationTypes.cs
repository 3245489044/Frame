using System;
using System.Collections.Generic;
using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{

    public class WorkflowExecutionStats
    {
        public string ExecutionId { get; set; }
        public SMExecutionStatus Status { get; set; }
        public string CurrentNode { get; set; }
        public List<string> CompletedNodes { get; set; }
        public List<string> FailedNodes { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string LastError { get; set; }
    }
}