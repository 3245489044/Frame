
using WFFunctionContext = TConfig.FunctionContext;

namespace WorkFlowMethod.Orchestration.Context.Models
{
    public class WorkflowContext : WFFunctionContext
    {
        public string NodeKey { get; }

        public string WorkflowId { get; }

        public Dictionary<string, Func<WorkflowContext, object>>? FunctionOverrides { get; set; }

        public Dictionary<string, Func<WorkflowContext, Task<object>>>? AsyncFunctionOverrides { get; set; }

        public WorkflowContext()
        {
            NodeKey = string.Empty;
            WorkflowId = string.Empty;
        }

        public WorkflowContext(
            IDictionary<string, object>? parameters = null,
            IDictionary<string, object>? data = null,
            string? executionId = null,
            string? nodeKey = null,
            string? workflowId = null)
        {
            NodeKey = nodeKey ?? string.Empty;
            WorkflowId = workflowId ?? string.Empty;

            if (parameters != null)
                Parameters = new Dictionary<string, object?>(parameters);
            if (data != null)
                Items = new Dictionary<string, object>(data);
            if (executionId != null)
                ExecutionId = executionId;

            if (nodeKey != null) Tags.Add(nodeKey);
            if (workflowId != null) Tags.Add(workflowId);
        }

        public IDictionary<string, object> Data => Items;

        public T GetData<T>(string key)
        {
            if (!Items.TryGetValue(key, out var value))
                throw new KeyNotFoundException($"未找到上下文数据: {key}");

            if (value is T typedValue)
                return typedValue;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"无法将上下文数据 '{key}' 从 {value?.GetType()} 转换为 {typeof(T)}", ex);
            }
        }

        public bool TryGetData<T>(string key, out T? value)
        {
            value = default;
            if (!Items.TryGetValue(key, out var obj)) return false;
            if (obj is T typed) { value = typed; return true; }
            try { value = (T)Convert.ChangeType(obj, typeof(T)); return true; }
            catch { return false; }
        }

        public void SetData<T>(string key, T value)
        {
            Items[key] = value!;
        }

        public new WorkflowContext CreateSubContext(string? nodeKey = null)
        {
            return new WorkflowContext(
                parameters: new Dictionary<string, object>(Parameters),
                data: new Dictionary<string, object>(Items),
                executionId: ExecutionId,
                nodeKey: nodeKey ?? NodeKey,
                workflowId: WorkflowId
            );
        }
    }
}
