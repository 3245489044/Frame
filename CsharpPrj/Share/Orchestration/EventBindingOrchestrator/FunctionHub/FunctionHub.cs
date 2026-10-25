
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TConfig;

namespace Share.EventBindingOrchestrator.FunctionHub
{
    public class FunctionHub : IFunctionHub
    {
        private readonly Dictionary<string, WorkflowDefinitionEntry> _workflows;
        private readonly ITrConfigRegistry _registry;
        private readonly ILogger<FunctionHub> _logger;

        public FunctionHub(
            ITrConfigRegistry registry,
            ILogger<FunctionHub>? logger = null)
        {
            _registry = PortGuard.Required(registry, nameof(registry));
            _logger = logger ?? NullLogger<FunctionHub>.Instance;
            _workflows = new Dictionary<string, WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public void RegisterWorkflows(Dictionary<string, WorkflowDefinitionEntry>? workflows)
        {
            if (workflows == null) return;

            foreach (var kvp in workflows)
            {
                _workflows[kvp.Key] = kvp.Value;
                _logger.LogDebug("FunctionHub registered workflow: {WorkflowName}, nodes={NodeCount}",
                    kvp.Key, kvp.Value.Nodes?.Count ?? 0);
            }
        }

        /// <summary>
        /// 解析函数（唯一裁决点）：工作流字典 → ITrConfigRegistry 单函数自动包装。
        /// R1 修复：原 RegisterFunctions 空实现已删除（只打日志不登记 = 名实不符的假注册面）。
        /// </summary>
        public FunctionResolution ResolveDetailed(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return new FunctionResolution(FunctionResolveStatus.InvalidName, null, "函数名为空");

            if (_workflows.TryGetValue(functionName, out var workflowDef))
                return new FunctionResolution(FunctionResolveStatus.Found, new WorkflowExecutionPlan(workflowDef), null);

            if (_registry.Find(functionName) != null)
            {
                var autoWrapped = AutoWrapAsWorkflow(functionName);
                _workflows[functionName] = autoWrapped;

                _logger.LogInformation(
                    "FunctionHub auto-wrapped function '{FunctionName}' as single-node workflow (from ITrConfigRegistry)",
                    functionName);

                return new FunctionResolution(FunctionResolveStatus.Found, new WorkflowExecutionPlan(autoWrapped), null);
            }

            var reason = $"函数未注册: '{functionName}'（工作流字典与 ITrConfigRegistry 均未命中）";
            _logger.LogError("FunctionHub resolve failed: {Reason}", reason);
            return new FunctionResolution(FunctionResolveStatus.NotRegistered, null, reason);
        }

        public WorkflowExecutionPlan? Resolve(string functionName) => ResolveDetailed(functionName).Plan;

        private WorkflowDefinitionEntry AutoWrapAsWorkflow(string functionName)
        {
            return new WorkflowDefinitionEntry
            {
                Id = functionName,
                Name = functionName,
                DisplayName = functionName,
                Nodes = new List<WorkflowNodeEntry>
                {
                    new()
                    {
                        Id = $"node-{functionName}",
                        FunctionName = functionName,
                        Parameters = null,
                        OutputKey = "value",
                        OutputTarget = null
                    }
                }
            };
        }

        public bool HasFunction(string functionName)
        {
            return _workflows.ContainsKey(functionName)
                || _registry.Contains(functionName);
        }

        public IEnumerable<string> GetWorkflowNames()
        {
            return _workflows.Keys.ToList();
        }
    }
}