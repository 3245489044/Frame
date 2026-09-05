
#region 命名空间引用
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using System.Runtime.CompilerServices;

using WorkFlow;
using WorkFlowMethod.FunctionExecutionFramework;
using WorkFlowMethod.FunctionExecutionFramework.Core;
using Fef = WorkFlowMethod.FunctionExecutionFramework.FunctionExecutionFramework;
using WorkFlowMethod.Orchestration.Context.Models;
using WorkFlowMethod.Orchestration.Context.Runtime;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;
using WorkFlowMethod.FunctionExecutionFramework.ExecutionPipeline;
using WorkFlowMethod.Infrastructure;
using WorkFlowMethod.Infrastructure.Constants;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Orchestration;
using WorkFlowMethod.Orchestration.Config;
using WorkFlowMethod.Orchestration.ModuleCreation;
using ParameterResolver.Builder;
using ParamResolver = ParameterResolver.Abstractions;
using WorkFlowMethod.Orchestration.Context.Execution;
using WorkFlowMethod.WorkFlowPipeline;
using WorkFlowMethod.Skeletons;
using WorkFlowMethod.Skeletons.NodeProcess;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.Graph;
using WorkFlowMethod.Skeletons.NodeProcess.Lifecycle;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Execution.Engine;
using WorkFlowMethod.Execution.Adapters;
using WorkFlowMethod.CSG;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.CallFrame;
using WorkFlowMethod.CallFrame.Core;
using WorkFlowMethod.CallFrame.Models;
using WorkFlowMethod.GraphFramework.Services;
using WorkFlowMethod.GraphFramework.Pipeline;
using WorkFlowMethod.GraphFramework.Deduplication;
using WorkFlowMethod.GraphFramework.Directives;
using GraphFramework.Directives;
using Share.LogicComposition.DefaultImplementations;
using TConfig;
using Modules.Registry;
using CSG.Unified;
using ExecutionFramework.Engine;
using ExecutionFramework.Abstractions;
using WorkflowExecution.Pipeline;
using WorkflowExecution.Abstractions;
using GraphFramework.Models;
using GraphFramework.Snapshots;
using GraphFoundation.Abstractions;
using Modules.Pipeline;
using SkeletonFlow;
using BmExecutionContext = TConfig.ExecutionContext;
using BmExecutionResult = TConfig.ExecutionResult;
using BmITrConfigRegistry = TConfig.ITrConfigRegistry;
using BmIFunctionUnit = TConfig.IFunctionUnit;
using BmPipelineDefinition = TConfig.TrPipeline;
using BmIPipelineBuilder = TConfig.IPipelineBuilder;

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;
using SkeletonPipelineBuilder = WorkFlowMethod.Skeletons.PipelineBuilder;
#endregion

namespace WorkFlowMethod.Usage
{
    public static class WorkFlowMethodUsage
    {

        #region ---- 第一部分：WorkflowUseage ----

        public static void EnsureInitialized()
        {
            var center = UnifiedRegistryCenter.Instance;
            if (!center.IsInitialized)
                throw new InvalidOperationException(
                    "UnifiedRegistryCenter 未初始化。请确保 FrameworkInitializer 已完成初始化。");
        }

        private static UnifiedRegistryCenter GetInitializedCenter()
        {
            var center = UnifiedRegistryCenter.Instance;
            if (!center.IsInitialized)
                throw new InvalidOperationException(
                    "UnifiedRegistryCenter 未初始化。请确保 FrameworkInitializer 已完成初始化。");
            return center;
        }

        private static IWorkflowRegistry GetRequiredWorkflowRegistry()
        {
            var center = GetInitializedCenter();
            if (center.Workflows == null)
                throw new InvalidOperationException(
                    "Workflows 注册表未创建。请确保在设置 WorkflowFactory 后已调用 UnifiedRegistryCenter.Initialize()，且注册函数库前初始化已完成。");
            return center.Workflows;
        }

        public static IWorkflowRegistry WorkFlowRegistry
        {
            get
            {
                var center = GetInitializedCenter();
                return center.Workflows;
            }
        }

        public static BmITrConfigRegistry BasicRegistry
        {
            get
            {
                var center = GetInitializedCenter();
                var reg = (BmITrConfigRegistry)center.Functions;

                var registeredCount = reg.GetRegisteredNames().Count();
                var regHash = RuntimeHelpers.GetHashCode(reg).ToString("X8");
                var regType = reg.GetType().FullName;
                WorkFlowMethodLog.Log("WorkflowMethodExecution_BasicRegistry_diag", new
                {
                    Hash = regHash,
                    Type = regType,
                    Count = registeredCount,
                    Message = "BasicRegistry getter 访问 UnifiedRegistryCenter.Functions"
                });

                if (registeredCount == 0)
                {
                    WorkFlowMethodLog.Log("WorkflowMethodExecution_Registry_empty", new
                    {
                        Message = "UnifiedRegistryCenter.Functions is empty! " +
                                  "Initialize() may not have been called before first access.",
                        RegistryHash = regHash
                    });

                    WorkFlowMethodLog.Log("WorkflowMethodExecution_Registry_diagnostic", new
                    {
                        Count = registeredCount,
                        RegistryHash = regHash,
                        StackTrace = Environment.StackTrace
                    });
                }

                return reg;
            }
        }


        public static PipelineExecutor Pipeline
        {
            get
            {
                var center = GetInitializedCenter();
                return (PipelineExecutor)center.Pipeline;
            }
        }

        public static FunctionExecutor Executor
        {
            get
            {
                var center = GetInitializedCenter();
                return (FunctionExecutor)center.Executor;
            }
        }

        public static void RegisterFunction(string name, Func<WfContext, object> func)
        {
            var registry = GetRequiredWorkflowRegistry();
            registry.Register(name, (Delegate)((FunctionContext ctx) =>
            {
                var wfCtx = new WfContext(
                    parameters: ctx.Parameters!,
                    data: ctx.Items,
                    executionId: ctx.ExecutionId,
                    nodeKey: null,
                    workflowId: null
                );
                return func(wfCtx);
            }));
        }

        public static void RegisterAsyncFunction(string name, Func<WfContext, Task<object>> func)
        {
            var registry = GetRequiredWorkflowRegistry();
            registry.Register(name, (Delegate)(async (FunctionContext ctx) =>
            {
                var wfCtx = new WfContext(
                    parameters: ctx.Parameters!,
                    data: ctx.Items,
                    executionId: ctx.ExecutionId,
                    nodeKey: null,
                    workflowId: null
                );
                // ConfigureAwait(false)：库注册的 async 回调不应捕获环境 SynchronizationContext，
                // 否则在带 SyncContext 的线程（如 GUI/调度上下文）被 DynamicInvoke 时，
                // 续延会编组回被阻塞的上下文造成异步死锁（ClineLibrary 记载的「UI 线程死锁根因」同类）。
                return await func(wfCtx).ConfigureAwait(false);
            }));
        }

        public static object CallFunction(string name, WfContext context)
        {
            return WorkFlowRegistry.Call(name, context)!;
        }

        public static Task<object> CallFunctionAsync(string name, WfContext context)
        {
            return WorkFlowRegistry.CallAsync(name, context)!;
        }

        public static async Task<BmExecutionResult> ExecuteAsync(
            string functionName, Dictionary<string, object> namedParameters, BmExecutionContext? context = null)

        {
            return await Executor.ExecuteAsync(functionName, namedParameters, context);
        }


        private static readonly ExecutionPipelineRunner _pipelineRunner = new();

        public static BmIPipelineBuilder CreatePipeline(string? name = null)
        {
            return _pipelineRunner.CreateBuilder(name);
        }

        public static Task<BmExecutionResult> ExecutePipelineAsync(
            BmPipelineDefinition pipeline, BmExecutionContext? context = null)
        {
            return _pipelineRunner.ExecutePipelineAsync(pipeline, context);
        }

        public static Task<BmExecutionResult> ExecuteStepAsync<TDelegate>(
            string stepName, TDelegate function, object? input = null)
            where TDelegate : Delegate
        {
            return _pipelineRunner.ExecuteStepAsync(stepName, function, input);
        }

        public static Task<BmExecutionResult> ExecuteStepsAsync<TDelegate>(
            params (string Name, TDelegate Function)[] steps)
            where TDelegate : Delegate
        {
            return _pipelineRunner.ExecuteStepsAsync(steps);
        }

        public static BmPipelineDefinition? LoadPipelineFromFile(string filePath)
        {
            return _pipelineRunner.LoadPipelineFromFile(filePath);
        }

        public static BmPipelineDefinition? LoadPipelineFromJson(string json)
        {
            return _pipelineRunner.LoadPipelineFromJson(json);
        }

        public static Task<BmExecutionResult> ExecuteFromFileAsync(
            string filePath, BmExecutionContext? context = null)
        {
            return _pipelineRunner.ExecuteFromFileAsync(filePath, context);
        }

        public static Task<BmExecutionResult> ExecuteFromJsonAsync(
            string json, BmExecutionContext? context = null)
        {
            return _pipelineRunner.ExecuteFromJsonAsync(json, context);
        }

        public static void SavePipelineToFile(BmPipelineDefinition pipeline, string filePath)
        {
            _pipelineRunner.SavePipelineToFile(pipeline, filePath);
        }

        public static IEnumerable<string> GetAllFunctionNames()
        {
            return WorkFlowRegistry.GetFunctionNames();
        }

        public static bool HasFunction(string name)
        {
            return WorkFlowRegistry.HasFunction(name);
        }

        #endregion


        #region ---- 第二部分：FunctionExecutionFramework ----

        public static DefaultWorkflowRegistry FefRegistry => Fef.Registry;

        public static PipelineExecutor FefPipeline => Fef.Pipeline;

        public static Task<BmExecutionResult> FefExecuteAsync(string functionName, object? parameters = null, BmExecutionContext? context = null)
            => Fef.ExecuteAsync(functionName, parameters, context);

        public static Task<BmExecutionResult> FefExecuteDelegateAsync(Delegate function, object? parameters = null, BmExecutionContext? context = null)
            => Fef.ExecuteAsync(function, parameters, context);

        public static Task<BmExecutionResult> FefExecuteUnitAsync(BmIFunctionUnit unit, object? input = null, BmExecutionContext? context = null)
            => Fef.ExecuteAsync(unit, input, context);

        public static BmIPipelineBuilder FefCreatePipeline(string? name = null)
            => Fef.CreatePipeline(name);

        public static Task<BmExecutionResult> FefExecutePipelineAsync(BmPipelineDefinition pipeline, BmExecutionContext? context = null)
            => Fef.ExecutePipelineAsync(pipeline, context);


        #endregion


        #region ---- 第三部分：WorkflowBuilder ----

        public static string BuildWorkflow(List<object> nodes, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.BuildWorkflow(nodes, quotedFields);

        public static List<object> ParseFromJson(string json)
            => WorkflowBuilder.ParseFromJson(json);

        public static List<dynamic> CreateNodesFromJson(string json)
            => WorkflowBuilder.CreateNodesFromJson(json);

        public static List<dynamic> CreateNodesFromQuotedJson(string json, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.CreateNodesFromQuotedJson(json, quotedFields);

        public static List<object> LoadFromFile(string filePath, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.LoadFromFile(filePath, quotedFields);

        public static List<dynamic> LoadNodesFromFile(string filePath, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.LoadNodesFromFile(filePath, quotedFields);

        public static Infrastructure.ValidationResult ValidateWorkflow<T>(List<T> nodes) where T : class
            => WorkflowBuilder.ValidateWorkflow(nodes);

        public static void SaveToFile(List<object> nodes, string filePath, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.SaveToFile(nodes, filePath, quotedFields);

        public static string BuildSingleNode(string symbol, string nodeId, string funcName, object[]? parameterKeys = null, object[]? resultKeys = null, string? label = null, HashSet<string>? quotedFields = null)
            => WorkflowBuilder.BuildSingleNode(symbol, nodeId, funcName, parameterKeys, resultKeys, label, quotedFields);

        public static void AddNodesFromStrings(List<object> nodes, params string[] nodeDefinitions)
            => WorkflowBuilder.AddNodesFromStrings(nodes, nodeDefinitions);

        public static string BuildFromStrings(params string[] nodeDefinitions)
            => WorkflowBuilder.BuildFromStrings(nodeDefinitions);

        public static string GetSummary(List<object> nodes)
            => WorkflowBuilder.GetSummary(nodes);

        public static void AddNode(List<object> nodes, string symbol, string nodeId, string funcName, object[]? parameterKeys = null, object[]? resultKeys = null, string? label = null, dynamic? nextNodeConfig = null)
            => WorkflowBuilder.AddNode(nodes, symbol, nodeId, funcName, parameterKeys, resultKeys, label, nextNodeConfig);

        public static dynamic CreateNextNodeConfig(Dictionary<string, object>? configs = null)
            => WorkflowBuilder.CreateNextNodeConfig(configs);

        public static dynamic CreateNodeConfig(string funcName, object[] parameterKeys, object[] resultKeys, Dictionary<string, object>? additionalConfigs = null)
            => WorkflowBuilder.CreateNodeConfig(funcName, parameterKeys, resultKeys, additionalConfigs);

        public static dynamic CreateNode(string symbol, string nodeId, string funcName, object[]? parameterKeys = null, object[]? resultKeys = null, string? label = null, dynamic? nextNodeConfig = null, Dictionary<string, object>? additionalProperties = null)
            => WorkflowBuilder.CreateNode(symbol, nodeId, funcName, parameterKeys, resultKeys, label, nextNodeConfig, additionalProperties);

        public static TConfig.ExecutionPlan ToShareWorkflow(List<object> nodes, string workflowId = "default")
            => WorkflowBuilderBridge.ToShareWorkflow(nodes, workflowId);

        public static List<object> ToOperationNodes(TConfig.ExecutionPlan definition)
            => WorkflowBuilderBridge.ToOperationNodes(definition);

        #endregion


        #region ---- 第四部分：ModuleCreation（原 RefactoredExecution）----


        public static ModuleCreationConfig CreateModuleConfig(string workflowId, string nodeKey, Dictionary<string, object>? parameters = null)
            => new ModuleCreationConfig(workflowId, nodeKey, 0, parameters != null ? System.Text.Json.JsonSerializer.Serialize(parameters) : "");

        public static IModuleCreator CreateDefaultModuleCreator()
            => new DefaultModuleCreator();

        public static ModuleCreationManager CreateModuleManager()
            => new ModuleCreationManager();

        #endregion


        #region ---- 第五部分：WorkFlowPipeline ----

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildCustomPipeline(IGraph<string, NodeConfig, object> graph)
            => SkeletonPipelineBuilder.BuildCustomPipeline(graph);

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildBranchPipeline()
            => SkeletonPipelineBuilder.BuildBranchPipeline();

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildParallelPipeline()
            => SkeletonPipelineBuilder.BuildParallelPipeline();

        public static PipelineContextAccessor CreatePipelineContextAccessor(WfPipelineContext pipelineContext)
            => new PipelineContextAccessor(pipelineContext);

        #endregion


        #region ---- 第六部分：NodeProcess ----

        public static NodeProcessPipelineBuilder CreateNodeProcessPipelineBuilder()
            => new NodeProcessPipelineBuilder();

        public static string GetNodeConfigType()
            => NodeConfigFactory.ConfigType.Default;


        public static void CompleteNodeConfig(NodeConfig config, string configType = NodeConfigFactory.ConfigType.Default)
            => NodeConfigFactory.CompleteConfig(config, configType);

        public static NodeChainSkeleton CreateNodeChainSkeleton()
            => new NodeChainSkeleton();

        public static ChainInfoWorkflowStorageManager CreateStorageManager()
            => new ChainInfoWorkflowStorageManager();

        public static WebCallManager CreateWebCallManager()
            => new WebCallManager();

        #endregion


        #region ---- 第七部分：Orchestration ----

        public static DefaultCompositionValidator CreateValidator()
            => ModularValidator.CreateValidator();

        public static Share.LogicComposition.Models.ValidationResult ValidateWorkflowChain(Chain<string, NodeConfig> chain)
            => ModularValidator.WorkflowValidator.ValidateWorkflowChain(chain);

        public static Share.LogicComposition.Models.ValidationResult ValidateWorkflowConnections(List<WorkflowConnection<string, string>> connections)
            => ModularValidator.ConnectionValidator.ValidateWorkflowConnections(connections);

        public static Share.LogicComposition.Models.ValidationResult ValidateChainConfig(ChainConfig config)
            => ModularValidator.ConfigValidator.ValidateChainConfig(config);

        #endregion


        #region ---- 第八部分：CSG ----

        public static CsgIntegrationService CreateCsgIntegrationService()
            => new CsgIntegrationService();

        public static CanonicalForm ChainToCanonical(Chain<string, NodeConfig> chain, CsgIntegrationService? service = null)
        {
            service ??= new CsgIntegrationService();
            return service.ChainToCanonical(chain);
        }

        public static global::WorkFlowMethod.CSG.ValidationResult ValidateWorkflowConsistency(WfPipelineContext context, CsgIntegrationService? service = null)
        {
            service ??= new CsgIntegrationService();
            return service.ValidateWorkflow(context);
        }

        public static string ExportToJson(WfPipelineContext context, CsgIntegrationService? service = null)
        {
            service ??= new CsgIntegrationService();
            return service.ExportToJson(context);
        }

        public static object? ResolveCsgUri(string uri, WfPipelineContext context, CsgIntegrationService? service = null)
        {
            service ??= new CsgIntegrationService();
            return service.ResolveUri(uri, context);
        }

        #endregion


        #region ---- 第九部分：Infrastructure ----

        public static TypedExecutionContext CreateExecutionContext(string name)
            => new TypedExecutionContext(name);

        public static TypedExecutionContext GetOrCreateContext(string name)
            => ContextFactory.GetOrCreate(name);

        public static bool TryGetContext(string name, out TypedExecutionContext? context)
            => ContextFactory.TryGet(name, out context);

        public static void RemoveContext(string name)
            => ContextFactory.Remove(name);

        public static void ClearAllContexts()
            => ContextFactory.Clear();

        public static dynamic ConvertToExpando(Dictionary<string, object> source)
            => ExpandoObjectHelper.ConvertDictionaryToExpando(source);

        public static string? GetExpandoString(dynamic expando, string key)
            => ExpandoObjectHelper.GetStringValue(expando, key);

        public static object? GetExpandoValue(dynamic expando, string key)
            => ExpandoObjectHelper.GetValue(expando, key);

        public static dynamic CreateExpando()
            => ExpandoObjectHelper.CreateExpando();

        public static Infrastructure.ValidationResult CreateValidationSuccess()
            => Infrastructure.ValidationResult.Success();

        public static Infrastructure.ValidationResult CreateValidationFailure(string error)
            => Infrastructure.ValidationResult.Failure(error);


        public static void LogAndSwallowException(string context, Exception ex)
            => ExceptionHandler.LogAndSwallow(context, ex);

        public static Task<T> FallbackAsync<T>(Func<Task<T>> operation, T fallbackValue, string context)
            => ExceptionHandler.FallbackAsync(operation, fallbackValue, context);

        public static T Fallback<T>(Func<T> operation, T fallbackValue, string context)
            => ExceptionHandler.Fallback(operation, fallbackValue, context);

        #endregion


        #region ---- 第十部分：CallFrame ----

        public static CallFrameService CreateCallFrameService()
            => new CallFrameService();

        public static WorkFlowMethod.CallFrame.Core.CallFrame CreateCallFrame(string nodeKey, string? parentFrameId = null)
            => new WorkFlowMethod.CallFrame.Core.CallFrame(nodeKey, parentFrameId);

        public static CallFrameNode CreateCallFrameNode(string nodeKey, object? input = null)
            => new CallFrameNode(nodeKey, null, nodeKey);
        #endregion

        #region ---- 第十二部分：GraphUsing ----

        public static GraphValidator CreateGraphValidator()
            => new GraphValidator();

        public static NodeChainService CreateNodeChainService()
            => new NodeChainService();

        public static ConfigDeduplicationProcessor CreateConfigDeduplicationProcessor()
            => new ConfigDeduplicationProcessor(null, null, null, null, null);

        public static DefaultDirectiveApplier<string, NodeConfig, object> CreateDefaultDirectiveApplier()
            => new DefaultDirectiveApplier<string, NodeConfig, object>();

        public static GraphUpdateDirective CreateGraphUpdateDirective(string nodeKey, string type = "replace")
            => type switch
            {
                "insert" => new InsertNodeDirective(),
                "delete" => new DeleteNodeDirective(),
                "replace" => new ReplaceNodeDirective(),
                "merge" => new MergeNodesDirective(),
                "redirect" => new RedirectEdgeDirective(),
                "composite" => new CompositeDirective(),
                _ => new ReplaceNodeDirective()
            };

        #endregion


        #region ---- 第十三部分：Config/WorkFlowsSet ----


        #endregion
    }
}
