#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Runtime
{
    public class AsyncWorkflowContext
    {
        private static readonly AsyncLocal<AsyncWorkflowContext> _current = new();

        public static AsyncWorkflowContext Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public string ExecutionId { get; set; }
        public string WorkflowId { get; set; }
        public Dictionary<string, object> Items { get; set; } = new();
        public CancellationToken CancellationToken { get; set; }
        public DateTime StartTime { get; set; }

        public T GetItem<T>(string key) =>
            Items.TryGetValue(key, out var value) && value is T typed ? typed : default;

        public void SetItem(string key, object value) => Items[key] = value;
    }
}
