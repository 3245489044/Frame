#region USINGS
using System;
using System.Threading;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Execution
{
    public class WorkflowExecutionContext
    {
        public string ExecutionId { get; set; }
        public string WorkflowId { get; set; }
        public string CurrentNode { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public DateTime StartTime { get; set; }
        public string ParentExecutionId { get; set; }
        public string ChainId { get; set; }
    }
}
