global using ExecutionResult = TConfig.ExecutionResult;

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Text.Json;

global using Modules.EventFramework.Abstractions;
global using TConfig;
global using TConfig.ContextFrame.Abstractions;

global using WorkflowExecution.Engine;

global using WorkFlowMethod.Diagnostics;

global using ParameterResolver.Builder;
global using ParameterResolver.Abstractions;
global using ParameterResolver.Converter;

// 统一上下文状态原语 + 纯裁决器 canonical（clinerules axioms §2/§3/§4/§5）。
// 原 WorkFlowMethod.LoopFramework.Axioms 中的实现已上移为 Share.TaskStatusPool.Loops 唯一权威（2026-08-13 统一上下文原语整体下沉）
// （SingleImplementation / TH-12），此处以类型别名保持既有引用兼容。
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
global using Scope = Share.TaskStatusPool.Loops.Scope;
global using ErrorClassifier = Share.TaskStatusPool.Loops.ErrorClassifier;
global using AxiomActionResult = Share.TaskStatusPool.Loops.AxiomActionResult;
global using AxiomRound = Share.TaskStatusPool.Loops.AxiomRound;
global using RoundLifecycle = Share.TaskStatusPool.Loops.RoundLifecycle;
