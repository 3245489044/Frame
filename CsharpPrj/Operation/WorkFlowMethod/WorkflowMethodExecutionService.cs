using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Guards;
using ExecutionFramework.Abstractions;
using ExecutionFramework.Engine;
using ExecutionFramework.Systems;
using ExecutionCenter.Abstractions;
using ExecutionCenter.Matrix;
using WorkFlowMethod.Execution.Messages;
using WorkflowExecution.Pipeline;
using Share.EventBindingOrchestrator.FunctionHub;

namespace WorkFlowMethod.Usage
{
    public class WorkflowMethodExecutionService : IEventExecutionService, TConfig.IFunctionExecutor
    {
        private readonly IFunctionHub _functionHub;
        private readonly IStepExecutor _stepExecutor;
        private readonly CallChainGuard _callChainGuard;
        private readonly RgeExecutionOrchestrator _rgeOrchestrator;

        public WorkflowMethodExecutionService(
            IFunctionHub functionHub,
            IStepExecutor stepExecutor,
            IFunctionExecutor atomExecutor,
            CallChainGuard? callChainGuard = null)
        {
            _functionHub = functionHub ?? throw new ArgumentNullException(nameof(functionHub));
            _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
            _callChainGuard = callChainGuard ?? new CallChainGuard();
            // 条件节点「非成功/非 bool ⇒ 假分支」的可见化出口（单点接线；不自建第二套日志通道）。
            ConditionSystem.Trace = static (messageId, data) => WorkFlowMethodLog.Log(messageId, data);
            // 工作流执行采用 RGE 内核；开启"失败不中断"对齐栈B 语义（失败仅记录，继续执行后续步骤）。
            var scheduler = RgeGraphSchedulerExtensions.CreateDefault(atomExecutor);
            scheduler.ContinueOnFailure = true;
            _rgeOrchestrator = new RgeExecutionOrchestrator(scheduler);
        }

        public void RegisterWorkflows(Dictionary<string, WorkflowDefinitionEntry>? workflows)
        {
            _functionHub.RegisterWorkflows(workflows);
        }

        public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, object?[]? parameters, CancellationToken cancellationToken = default)
        {
            var namedParameters = ConvertToDictionary(parameters);
            return await ExecuteFunctionAsync(functionName, namedParameters, cancellationToken);
        }

        public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _callChainGuard.ProtectAsync(functionName, async () =>
                {
                    var plan = _functionHub.Resolve(functionName);
                    if (plan == null)
                    {
                        WorkFlowMethodLog.Log("Workflow_DefinitionNotFound", new
                        {
                            functionName,
                            suggestion = "请确保 FunctionHub 已正确初始化并注册了工作流定义"
                        });
                        return ExecutionResult.Failure(
                            $"Workflow definition '{functionName}' not found. " +
                            "All functions must be registered as workflows before execution.");
                    }

                    return await ExecuteWorkflowAsync(plan.WorkflowDef, namedParameters, cancellationToken);
                });
            }
            catch (CallChainExceededException ex)
            {
                return ExecutionResult.Failure(
                    $"Function '{functionName}' execution aborted: {ex.Message}");
            }
        }

        private async Task<ExecutionResult> ExecuteWorkflowAsync(
            WorkflowDefinitionEntry workflowDef,
            Dictionary<string, object> namedParameters,
            CancellationToken cancellationToken = default)
        {
            if (workflowDef.Nodes == null || workflowDef.Nodes.Count == 0)
            {
                return ExecutionResult.Failure(
                    $"Workflow '{workflowDef.Name}' has no nodes");
            }

            // 结构前置：叶子节点必须有可解析的函数引用。否则 RGE 只会报
            // 「Entity 'x' 的 FuncRef 为空」，丢失工作流/节点归因（声明键拼写错误的唯一可见面）。
            var invalidNode = FindUnresolvedNode(workflowDef.Nodes);
            if (invalidNode != null)
            {
                return ExecutionResult.Failure(
                    $"Workflow '{workflowDef.Name}' node '{invalidNode}' 缺少 functionName" +
                    "（声明键须为 functionName；容器节点须声明 children/elseChildren）");
            }

            try
            {
                // _stepSelector 起点语义：从选中步骤起执行（对齐栈B CanExecute: index >= StartIndex）。
                var startIndex = ResolveStartIndex(workflowDef, namedParameters);
                var effectiveNodes = workflowDef.Nodes
                    .Where(n => n != null)
                    .Skip(startIndex)
                    .ToList();

                var plan = new TConfig.ExecutionPlan
                {
                    Id = workflowDef.Id,
                    Name = workflowDef.Name,
                    ResultMode = TConfig.ResultCollectionMode.Single,
                    Steps = effectiveNodes
                        .Select(n => BuildStepDefinition(n!, namedParameters))
                        .ToList(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["OutputMapping"] = workflowDef.OutputMapping ?? new Dictionary<string, string>()
                    }
                };

                var context = new TConfig.ExecutionContext
                {
                    WorkflowId = workflowDef.Id,
                    NodeKey = workflowDef.Name,
                    Parameters = new Dictionary<string, object>(namedParameters),
                    Items = new Dictionary<string, object>
                    {
                        ["workflowName"] = workflowDef.Name
                    }
                };

                var result = await _rgeOrchestrator.OrchestrateAsync(plan, context, cancellationToken);

                if (!result.IsSuccess)
                {
                    return ExecutionResult.Failure(
                        $"Workflow '{workflowDef.Name}' failed: {result.ErrorMessage}");
                }

                return result;
            }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Log("WorkflowMethodExecution_Rge_Error", new
                {
                    workflowName = workflowDef.Name,
                    error = ex.Message
                });
                return ExecutionResult.Failure(
                    $"Workflow '{workflowDef.Name}' RGE execution error: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析工作流起点：优先 <c>_stepSelector</c> 命名参数，其次工作流静态 StepSelector，
        /// 按节点 Id 匹配返回索引；未匹配或空则从 0 开始。
        /// </summary>
        private static int ResolveStartIndex(
            WorkflowDefinitionEntry def,
            Dictionary<string, object> namedParameters)
        {
            var selector = namedParameters.TryGetValue("_stepSelector", out var sel)
                ? sel?.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(selector))
                selector = def.StepSelector;

            if (string.IsNullOrWhiteSpace(selector))
                return 0;

            for (int i = 0; i < def.Nodes.Count; i++)
            {
                if (def.Nodes[i].Id.Equals(selector, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        /// <summary>递归查找不可解析节点（叶子缺 functionName 且无子节点）；返回节点 Id 或 null。</summary>
        private static string? FindUnresolvedNode(List<WorkflowNodeEntry> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (!node.HasExecutable)
                    return string.IsNullOrWhiteSpace(node.Id) ? "(no-id)" : node.Id;
                if (node.Children is { Count: > 0 } children)
                {
                    var bad = FindUnresolvedNode(children);
                    if (bad != null) return bad;
                }
                if (node.ElseChildren is { Count: > 0 } elseChildren)
                {
                    var bad = FindUnresolvedNode(elseChildren);
                    if (bad != null) return bad;
                }
            }
            return null;
        }

        /// <summary>
        /// 递归构建步骤（B0）：节点树（children/elseChildren）原样保留为 <see cref="TConfig.StepDefinition.Children"/> /
        /// <see cref="TConfig.StepDefinition.FalseChildren"/>，参数按节点自身 InputKeys 过滤后递归注入。
        /// </summary>
        private static TConfig.StepDefinition BuildStepDefinition(
            WorkflowNodeEntry node,
            Dictionary<string, object> namedParameters)
        {
            var step = node.ToStepDefinition();
            step.Parameters = BuildStepParameters(node, namedParameters);

            if (node.Children is { Count: > 0 })
            {
                step.Children = node.Children
                    .Where(c => c != null)
                    .Select(c => BuildStepDefinition(c!, namedParameters))
                    .ToList();
            }

            if (node.ElseChildren is { Count: > 0 })
            {
                step.FalseChildren = node.ElseChildren
                    .Where(c => c != null)
                    .Select(c => BuildStepDefinition(c!, namedParameters))
                    .ToList();
            }

            return step;
        }

        /// <summary>
        /// 构建步骤参数：节点声明参数为底（字面量/占位），命名参数按 InputKeys 白名单覆盖（对齐栈B inputKeys 过滤语义）；
        /// 未声明 InputKeys 时命名参数全量覆盖。声明参数此前被整体丢弃，导致节点级字面量无法随工作流下发。
        /// </summary>
        private static Dictionary<string, object> BuildStepParameters(
            WorkflowNodeEntry node,
            Dictionary<string, object> namedParameters)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (node.Parameters is Dictionary<string, object> declared)
            {
                foreach (var kvp in declared)
                    result[kvp.Key] = kvp.Value;
            }
            else if (node.Parameters is System.Text.Json.JsonElement element
                     && element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                    result[prop.Name] = ConvertJsonToObject(prop.Value)!;
            }

            if (node.InputKeys is { Count: > 0 })
            {
                foreach (var key in node.InputKeys)
                {
                    if (namedParameters.TryGetValue(key, out var v))
                        result[key] = v;
                }
                return result;
            }

            foreach (var kvp in namedParameters)
                result[kvp.Key] = kvp.Value;

            return result;
        }

        private static Dictionary<string, object> MergeNodeParameters(
            Dictionary<string, object> namedParameters,
            object? nodeParameters)
        {
            var merged = new Dictionary<string, object>(namedParameters);

            if (nodeParameters == null)
                return merged;

            if (nodeParameters is Dictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                    if (!merged.ContainsKey(kvp.Key))
                        merged[kvp.Key] = kvp.Value;
            }
            else if (nodeParameters is System.Text.Json.JsonElement json
                     && json.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in json.EnumerateObject())
                    if (!merged.ContainsKey(prop.Name))
                        merged[prop.Name] = ConvertJsonToObject(prop.Value);
            }

            return merged;
        }


        public WorkflowDefinitionEntry? FindWorkflow(string functionName)
        {
            return _functionHub.Resolve(functionName)?.WorkflowDef;
        }


        public async Task<ExecutionResult> ExecuteEventAsync(string eventName, object? sender, object? args)
        {
            var parameters = new Dictionary<string, object>();
            if (sender != null) parameters["sender"] = sender;
            if (args != null) parameters["args"] = args;
            return await ExecuteFunctionAsync(eventName, parameters);
        }

        async Task<ExecutionResult> IEventExecutionService.ExecuteFunctionAsync(string functionName, Dictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            return await ExecuteFunctionAsync(functionName, ConvertToDictionary(parameters), cancellationToken);
        }


        async Task<FunctionExecutionResult> IFunctionExecutor.ExecuteFunctionAsync(string functionName, Dictionary<string, object?> parameters)
        {
            var result = await ExecuteFunctionAsync(functionName, ConvertToDictionary(parameters));
            return new FunctionExecutionResult
            {
                IsSuccess = result.IsSuccess,
                Value = result.Value,
                ErrorMessage = result.ErrorMessage,
                ExecutionTime = result.ExecutionTime
            };
        }

        bool IFunctionExecutor.HasFunction(string functionName) => HasFunction(functionName);

        public bool HasFunction(string functionName)
        {
            return _functionHub.HasFunction(functionName);
        }


        private static object? ConvertJsonToObject(System.Text.Json.JsonElement element)
        {
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => element.GetString(),
                System.Text.Json.JsonValueKind.Number =>
                    element.TryGetInt64(out var l) ? l : element.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Array =>
                    element.EnumerateArray().Select(ConvertJsonToObject).ToList(),
                System.Text.Json.JsonValueKind.Object =>
                    element.EnumerateObject()
                        .ToDictionary(p => p.Name, p => ConvertJsonToObject(p.Value)),
                _ => element.GetRawText()
            };
        }

        private static Dictionary<string, object> ConvertToDictionary(object?[]? parameters)
        {
            var result = new Dictionary<string, object>();
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    result[$"param{i}"] = parameters[i] ?? string.Empty;
                }
            }
            return result;
        }

        private static Dictionary<string, object> ConvertToDictionary(Dictionary<string, object?> parameters)
        {
            var result = new Dictionary<string, object>(parameters.Count);
            foreach (var kvp in parameters)
            {
                result[kvp.Key] = kvp.Value ?? string.Empty;
            }
            return result;
        }


        Task<ExecutionResult> TConfig.IFunctionExecutor.ExecuteAsync(string functionName, object?[]? parameters, TConfig.IExecutionContext? context)
            => ExecuteFunctionAsync(functionName, parameters);

        Task<ExecutionResult> TConfig.IFunctionExecutor.ExecuteAsync(string functionName, Dictionary<string, object> namedParameters, TConfig.IExecutionContext? context)
            => ExecuteFunctionAsync(functionName, namedParameters);

        Task<ExecutionResult> TConfig.IFunctionExecutor.ExecuteAsync(Delegate function, object? parameters, TConfig.IExecutionContext? context)
            => throw new NotSupportedException("Direct delegate execution is not supported by WorkflowMethodExecutionService.");

        Task<ExecutionResult> TConfig.IFunctionExecutor.ExecuteAsync(IFunctionUnit unit, object? input, TConfig.IExecutionContext? context)
            => ExecuteFunctionAsync(unit.Name, new[] { input });

        Task<ExecutionResult> TConfig.IFunctionExecutor.ExecuteAsync(ITrConfig config, object?[]? parameters, TConfig.IExecutionContext? context)
            => ExecuteFunctionAsync(config.Name, parameters);

        async Task<ExecutionResult<TResult>> TConfig.IFunctionExecutor.ExecuteAsync<TResult>(string functionName, object?[]? parameters, TConfig.IExecutionContext? context)
        {
            var result = await ExecuteFunctionAsync(functionName, parameters);
            return ExecutionResult<TResult>.From(result);
        }

     }
}