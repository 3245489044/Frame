#region USINGS
using System;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Execution
{
    public interface INodeExecutor
    {
        Task<NodeExecutionResult> ExecuteAsync(NodeConfig node, WorkflowExecutionContext context);
        bool CanExecute(NodeConfig node);
    }

    public class NodeExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public object Output { get; set; }
        public Exception Error { get; set; }
        public string NextNodeKey { get; set; }
    }

    public class DefaultNodeExecutor : INodeExecutor
    {
        public async Task<NodeExecutionResult> ExecuteAsync(NodeConfig node, WorkflowExecutionContext context)
        {
            try
            {
                var cancellationToken = context?.CancellationTokenSource?.Token ?? CancellationToken.None;
                await Task.Delay(100, cancellationToken);
                return new NodeExecutionResult
                {
                    Success = true,
                    Message = $"节点 {node.basicConfig?.Callkey ?? node.Id} 执行成功",
                    NextNodeKey = node.nextNodeConfig?.NextNodeKey
                };
            }
            catch (Exception ex)
            {
                return new NodeExecutionResult
                {
                    Success = false,
                    Message = $"节点 {node.basicConfig?.Callkey ?? node.Id} 执行失败",
                    Error = ex
                };
            }
        }

        public bool CanExecute(NodeConfig node) => node != null;
    }
}
