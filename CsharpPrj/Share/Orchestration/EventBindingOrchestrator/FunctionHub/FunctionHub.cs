
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
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? NullLogger<FunctionHub>.Instance;
            _workflows = new Dictionary<string, WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public void RegisterFunctions(IEnumerable<FunctionRegistrationEntry>? functions)
        {
            if (functions == null) return;

            foreach (var func in functions)
            {
                _logger.LogDebug("FunctionHub registered function: {FunctionName}, type={Type}, enabled={Enabled}",
                    func.Name, func.Type, func.Enabled);
            }
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

        public WorkflowExecutionPlan? Resolve(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return null;

            if (_workflows.TryGetValue(functionName, out var workflowDef))
            {
                return new WorkflowExecutionPlan(workflowDef);
            }

            if (_registry.Find(functionName) != null)
            {
                var autoWrapped = AutoWrapAsWorkflow(functionName);
                _workflows[functionName] = autoWrapped;

                _logger.LogInformation(
                    "FunctionHub auto-wrapped function '{FunctionName}' as single-node workflow (from ITrConfigRegistry)",
                    functionName);

                return new WorkflowExecutionPlan(autoWrapped);
            }

            _logger.LogWarning(
                "FunctionHub could not resolve function '{FunctionName}': not found in workflows or registry",
                functionName);
            return null;
        }

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