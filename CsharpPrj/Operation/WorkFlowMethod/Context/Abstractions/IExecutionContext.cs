#region USINGS
using System;
using System.Threading;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Abstractions
{
    public interface IExecutionContext
    {
        string ExecutionId { get; }

        string CurrentNode { get; }

        CancellationTokenSource CancellationTokenSource { get; }

        DateTime StartTime { get; }

        string ParentExecutionId { get; }

        string ChainId { get; }
    }
}
