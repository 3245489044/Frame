using System;
using System.Collections.Generic;
using WorkflowExecution.Abstractions;

namespace WorkFlowMethod.Infrastructure.Abstractions
{
    public class GenericWorkflowStorageManager<TWorkflow> where TWorkflow : class
    {
        private readonly Dictionary<string, TWorkflow> _workflows = new();
        private readonly Dictionary<string, WorkflowReference<TWorkflow>> _references = new();
        private readonly Dictionary<string, List<Action<TWorkflow>>> _observers = new();
        private readonly Dictionary<string, List<Action<string>>> _removalObservers = new();
        private readonly Dictionary<string, List<string>> _nodeToWorkflowMap = new();
        private readonly INodeConfigExtractor<TWorkflow> _extractor;
        private readonly Func<TWorkflow, string> _idGenerator;

        public GenericWorkflowStorageManager()
        {
            _extractor = null;
            _idGenerator = null;
        }

        public GenericWorkflowStorageManager(INodeConfigExtractor<TWorkflow> extractor, Func<TWorkflow, string> idGenerator = null)
        {
            _extractor = extractor;
            _idGenerator = idGenerator;
        }

        public TWorkflow GetWorkflow(string workflowId)
        {
            _workflows.TryGetValue(workflowId, out var workflow);
            return workflow;
        }

        public IEnumerable<string> GetAllWorkflowIds() => _workflows.Keys;

        /// <summary>
        /// 注册或更新工作流。P0-3：身份必须可复算 —— 无 <c>_idGenerator</c> 时不得每次生成新 Guid
        /// （否则 RegisterOrUpdate 永远 Insert，更新退化为新增行）。
        /// </summary>
        public string RegisterOrUpdateWorkflow(TWorkflow workflow)
        {
            if (workflow is null) throw new ArgumentNullException(nameof(workflow));
            if (_idGenerator is null)
            {
                throw new InvalidOperationException(
                    "未配置工作流 id 生成器：RegisterOrUpdateWorkflow 需要稳定身份。请使用显式 id 重载或注入 idGenerator。");
            }

            return RegisterOrUpdateWorkflow(workflow, _idGenerator(workflow));
        }

        /// <summary>以显式身份注册或更新（同一 id 二次调用 = 更新，不新增行）。</summary>
        public string RegisterOrUpdateWorkflow(TWorkflow workflow, string workflowId)
        {
            if (workflow is null) throw new ArgumentNullException(nameof(workflow));
            if (string.IsNullOrWhiteSpace(workflowId))
                throw new ArgumentException("工作流 id 不能为空", nameof(workflowId));

            _workflows[workflowId] = workflow;
            UpdateNodeToWorkflowMap(workflowId, workflow);
            NotifyObservers(workflowId, workflow);
            return workflowId;
        }

        private void UpdateNodeToWorkflowMap(string workflowId, TWorkflow workflow)
        {
            if (_extractor == null) return;

            foreach (var nodeId in _extractor.GetNodeIds(workflow))
            {
                if (!_nodeToWorkflowMap.ContainsKey(nodeId))
                    _nodeToWorkflowMap[nodeId] = new List<string>();
                if (!_nodeToWorkflowMap[nodeId].Contains(workflowId))
                    _nodeToWorkflowMap[nodeId].Add(workflowId);
            }
        }

        public void RemoveWorkflow(string workflowId)
        {
            _workflows.Remove(workflowId);
            _references.Remove(workflowId);
            NotifyRemovalObservers(workflowId);
        }

        public WorkflowReference<TWorkflow> GetWorkflowReference(string workflowId)
        {
            _references.TryGetValue(workflowId, out var reference);
            return reference;
        }

        public IReadOnlyDictionary<string, WorkflowReference<TWorkflow>> GetReferences()
            => _references as IReadOnlyDictionary<string, WorkflowReference<TWorkflow>>;

        public List<string> FindWorkflowsByNode(string nodeId)
        {
            _nodeToWorkflowMap.TryGetValue(nodeId, out var workflowIds);
            return workflowIds ?? new List<string>();
        }

        public string GetWorkflowIdForNode(string nodeId)
        {
            var workflows = FindWorkflowsByNode(nodeId);
            return workflows.Count > 0 ? workflows[0] : null;
        }

        public bool IsNodeInAnyWorkflow(string nodeId)
            => _nodeToWorkflowMap.ContainsKey(nodeId);

        public void RegisterObserver(string workflowId, Action<TWorkflow> callback)
        {
            if (!_observers.ContainsKey(workflowId))
                _observers[workflowId] = new List<Action<TWorkflow>>();
            _observers[workflowId].Add(callback);
        }

        public void UnregisterObserver(string workflowId, Action<TWorkflow> callback)
        {
            if (_observers.TryGetValue(workflowId, out var list))
                list.Remove(callback);
        }

        public void RegisterRemovalObserver(string workflowId, Action<string> callback)
        {
            if (!_removalObservers.ContainsKey(workflowId))
                _removalObservers[workflowId] = new List<Action<string>>();
            _removalObservers[workflowId].Add(callback);
        }

        public void UnregisterRemovalObserver(string workflowId, Action<string> callback)
        {
            if (_removalObservers.TryGetValue(workflowId, out var list))
                list.Remove(callback);
        }

        private void NotifyObservers(string workflowId, TWorkflow workflow)
        {
            if (_observers.TryGetValue(workflowId, out var list))
                foreach (var observer in list)
                    observer(workflow);
        }

        private void NotifyRemovalObservers(string workflowId)
        {
            if (_removalObservers.TryGetValue(workflowId, out var list))
                foreach (var observer in list)
                    observer(workflowId);
        }
    }

    public class WorkflowReference<TWorkflow>
    {
        public string WorkflowId { get; set; }
        public TWorkflow Workflow { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastAccessedAt { get; set; }
    }
}
