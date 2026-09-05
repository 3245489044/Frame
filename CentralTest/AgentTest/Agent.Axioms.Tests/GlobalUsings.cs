// Agent.Axioms.Tests 全局类型别名 —— 与 Agent src GlobalUsings.cs 对齐（SingleImplementation / TH-12）。
// 统一上下文原语 / HierarchySupport 支撑 DTO / HierarchyMethod 三层契约均经本文件桥接，
// 使测试侧沿用旧命名空间 using 即可解析已下沉类型（含 2026-08-25 Workflows 下沉）。
global using FailureBudget = Share.TaskStatusPool.Loops.FailureBudget;
global using ProgressToken = Share.TaskStatusPool.Loops.ProgressToken;
global using ControlFlag = Share.TaskStatusPool.Loops.ControlFlag;
global using ILoopState = Share.TaskStatusPool.Loops.ILoopState;
global using EvalOutcome = Share.TaskStatusPool.Loops.EvalOutcome;
global using LoopDecision = Share.TaskStatusPool.Loops.LoopDecision;
global using ErrorClass = Share.TaskStatusPool.Loops.ErrorClass;
global using ProgressUpdater = Share.TaskStatusPool.Loops.ProgressUpdater;
global using BudgetEscalator = Share.TaskStatusPool.Loops.BudgetEscalator;
global using TerminationDecider = Share.TaskStatusPool.Loops.TerminationDecider;
global using SyncProtocol = Share.TaskStatusPool.Loops.SyncProtocol;
global using DeterministicCoreCopier = Share.TaskStatusPool.Loops.DeterministicCoreCopier;
global using ContinuousFailureRule = Share.TaskStatusPool.Loops.ContinuousFailureRule;
global using Scope = Share.TaskStatusPool.Loops.Scope;
global using ErrorClassifier = Share.TaskStatusPool.Loops.ErrorClassifier;
global using ExecutionDisposition = Share.TaskStatusPool.Routing.ExecutionDisposition;
global using RuleResolution = Share.TaskStatusPool.Routing.RuleResolution;
global using TaskRoutingDecision = Share.TaskStatusPool.Routing.TaskRoutingDecision;
global using ITaskAutonomy = Share.TaskStatusPool.Routing.ITaskAutonomy;
global using TaskRouter = Share.TaskStatusPool.Routing.TaskRouter;
global using AxiomActionResult = Share.TaskStatusPool.Loops.AxiomActionResult;
global using AxiomRound = Share.TaskStatusPool.Loops.AxiomRound;
global using RoundLifecycle = Share.TaskStatusPool.Loops.RoundLifecycle;

// ===== HierarchySupport 支撑 DTO 全局别名（2026-08-25 下沉）=====
global using SchedulingMode = Share.HierarchySupport.SchedulingMode;
global using StrategyDescriptor = Share.HierarchySupport.StrategyDescriptor;
global using DataSpace = Share.HierarchySupport.DataSpace;
global using IDataSpace = Share.HierarchySupport.IDataSpace;
global using DataScope = Share.HierarchySupport.DataScope;
global using PortSpec = Share.HierarchySupport.PortSpec;
global using DataContract = Share.HierarchySupport.DataContract;
global using DataContractBuilder = Share.HierarchySupport.DataContractBuilder;
global using DataContractRegistry = Share.HierarchySupport.DataContractRegistry;
global using DataDirection = Share.HierarchySupport.DataDirection;
global using AgentRuntimeSnapshot = Share.HierarchySupport.AgentRuntimeSnapshot;
global using ExecutorRunStatus = Share.HierarchySupport.ExecutorRunStatus;
global using RuntimeStage = Share.HierarchySupport.RuntimeStage;

// ===== HierarchyMethod.Tasks 实体全局别名（2026-08-25 迁入框架）=====
global using TaskRecord = Operation.HierarchyMethod.Tasks.TaskRecord;
global using ExecutionNode = Operation.HierarchyMethod.Tasks.ExecutionNode;
global using AgentLoopState = Operation.HierarchyMethod.Tasks.Loops.AgentLoopState;
global using IAgentLoopState = Operation.HierarchyMethod.Tasks.Loops.IAgentLoopState;

// ===== HierarchyMethod.Nodes 契约全局别名（2026-08-25 迁入框架）=====
global using NodeDefinition = Operation.HierarchyMethod.Nodes.NodeDefinition;
global using AgentNode = Operation.HierarchyMethod.Nodes.NodeDefinition;
global using NodeOutput = Operation.HierarchyMethod.Nodes.NodeOutput;
global using NodeOutputKeys = Operation.HierarchyMethod.Nodes.NodeOutputKeys;
global using NodeKeys = Operation.HierarchyMethod.Nodes.NodeKeys;
global using INodeOutputStore = Operation.HierarchyMethod.Nodes.INodeOutputStore;
global using NodeExecutionRecord = Operation.HierarchyMethod.Nodes.NodeExecutionRecord;
global using NodeOutputCodec = Operation.HierarchyMethod.Nodes.NodeOutputCodec;
global using NodeOutputDoc = Operation.HierarchyMethod.Nodes.NodeOutputDoc;
global using NodeOutputPortValue = Operation.HierarchyMethod.Nodes.NodeOutputPortValue;
global using TaskGraphTaskSpec = Operation.HierarchyMethod.Nodes.TaskGraph.TaskGraphTaskSpec;
global using ITaskGraphTaskStore = Operation.HierarchyMethod.Nodes.TaskGraph.ITaskGraphTaskStore;
global using NodeOutputStore = Operation.HierarchyMethod.Nodes.NodeOutputStore;

// ===== HierarchyMethod.Nodes.Workflows 契约全局别名（2026-08-25 下沉）=====
global using RoundSpec = Operation.HierarchyMethod.Nodes.Workflows.RoundSpec;
global using RoundStep = Operation.HierarchyMethod.Nodes.Workflows.RoundStep;
global using RoundSpecSource = Operation.HierarchyMethod.Nodes.Workflows.RoundSpecSource;
global using RoundSpecSuccessCriterion = Operation.HierarchyMethod.Nodes.Workflows.RoundSpecSuccessCriterion;
global using BindingStep = Operation.HierarchyMethod.Nodes.Workflows.BindingStep;
global using BindingContainer = Operation.HierarchyMethod.Nodes.Workflows.BindingContainer;

// ===== HierarchyMethod.History 契约全局别名（2026-08-25 迁入框架）=====
global using HistoryRecord = Operation.HierarchyMethod.History.HistoryRecord;
global using HistoryClass = Operation.HierarchyMethod.History.HistoryRecord;
global using HistoryTaskLink = Operation.HierarchyMethod.History.HistoryTaskLink;
global using IHistoryRelationManager = Operation.HierarchyMethod.History.IHistoryRelationManager;
global using HistoryRelationManager = Operation.HierarchyMethod.History.HistoryRelationManager;

