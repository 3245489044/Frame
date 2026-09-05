
using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;
using ECS.Components;
using BackEndDriven.Diagnostics;
using BackEndDriven.Utilities;
using FrameworkAbstractions.ComponentRegistry;
using Modules.Parsing.Models;
using TConfig;

namespace BackEndDriven.CommandExecution;

public sealed class CommandInputPort
{
    private readonly EntityManager _entityManager;
    private readonly CommandSystemCoordinator _coordinator;
    private readonly ParseCommandSystem _parseSystem;

    /// <summary>
    /// F4.3（fail-stop·策略位）：作用域渲染失败时脚本提前终止，避免下游 validate 链无意义执行。
    /// 默认关（未运行时实测前不改变既有长脚本行为，S2 实测成本/收益后再决定默认开）。
    /// </summary>
    private readonly bool _failFastOnScopeError;

    public CommandInputPort(EntityManager entityManager, bool failFastOnScopeError = false)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _coordinator = new CommandSystemCoordinator(entityManager);
        _parseSystem = _coordinator.ParseSystem;
        _failFastOnScopeError = failFastOnScopeError;
    }

    public CommandSystemCoordinator Coordinator => _coordinator;

    public EntityManager EntityManager => _entityManager;

    public void LoadCommandLines(string[] commandLines)
    {
        ArgumentNullException.ThrowIfNull(commandLines);

        BackEndLog.Log("CommandInputPort_LoadCommandLines", new { count = commandLines.Length });

        for (int i = 0; i < commandLines.Length; i++)
        {
            _parseSystem.ParseAndCreateEntity(commandLines[i], i);
        }

        BackEndLog.Log("CommandInputPort_CommandLinesLoaded",
            new { commandCount = commandLines.Length });
    }

    public void LoadMetaCommands(List<global::Modules.Parsing.Models.MetaCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        BackEndLog.Log("CommandInputPort_LoadMetaCommands", new { count = commands.Count });

        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (cmd == null) continue;

            _entityManager.CreateCommandEntity(
                cmd.Action ?? "",
                cmd.Target,
                cmd.Parameters,
                cmd.OriginalCommand ?? $"{cmd.Action} {cmd.Target}".Trim(),
                i);
        }

        BackEndLog.Log("CommandInputPort_MetaCommandsLoaded",
            new { commandCount = commands.Count });
    }

    public Entity ExecuteSingle(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);

        // 优先事件平台 workflow 执行（含 middleware 管道）；成功后返回执行结果实体。
        var workflowEntity = AsyncHelper.RunSync(() => ExecuteSingleWorkflowAsync(commandText), 30_000);
        if (workflowEntity != null)
            return workflowEntity.Value;

        // 回退：本地命令系统解析并执行。
        var entity = _parseSystem.ParseAndCreateEntity(commandText, 0);
        if (!_entityManager.HasComponent<CommandStatusComponent>(entity))
            return entity;

        var status = _entityManager.GetComponent<CommandStatusComponent>(entity);
        if (status.State == CommandState.Pending)
        {
            _coordinator.Update(entity);
        }

        // 统一产出形态：本地路径结果也补挂 ExecutionResultComponent，
        // 与 workflow 路径一致（消除双路径返回形态不一致）。
        // FunctionName 显式传 commandText，与 workflow 路径口径严格一致
        // （不隐式依赖 meta.RawCommand——解析器未来规范化 RawCommand 也不影响本口径）。
        _entityManager.AddOrSetComponent(entity, BuildExecutionResultComponent(entity, commandText));
        return entity;
    }

    private ExecutionResultComponent BuildExecutionResultComponent(Entity entity, string commandText)
    {
        var status = _entityManager.TryGetComponent<CommandStatusComponent>(entity, out var s) ? s : default;
        var result = _entityManager.TryGetComponent<CommandResultComponent>(entity, out var r) ? r : default;

        return new ExecutionResultComponent
        {
            FunctionName = commandText,
            Result = result.ResultValue,
            Success = status.State == CommandState.Success,
            ErrorMessage = status.ErrorMessage,
            SourceEntityId = entity.Id,
            CompletedTimestamp = DateTime.UtcNow
        };
    }

    private async Task<Entity?> ExecuteSingleWorkflowAsync(string commandText)
    {
        try
        {
            // 统一执行入口（聚拢 R:1.2）：IEventPlatform 解析 + InvokeFunctionAsync + ExecCommand/超时 归一。
            // InvokeAsync 在 IEventPlatform 未注册时 Result=null → 触发本地命令系统回退。
            var namedParams = new Dictionary<string, object?>
            {
                ["value"] = commandText,
                ["command"] = commandText
            };
            var invocation = await EventPlatformExecutor.InvokeAsync(commandText, namedParams)
                .ConfigureAwait(false);
            var result = invocation.Result;
            if (result == null)
                return null;

            var resultEntity = _entityManager.CreateEntity();
            _entityManager.AddComponent(resultEntity, new ExecutionResultComponent
            {
                FunctionName = commandText,
                Success = result.IsSuccess,
                Result = result.Output,
                ErrorMessage = result.ErrorMessage,
                CompletedTimestamp = DateTime.UtcNow
            });
            return resultEntity;
        }
        catch (Exception ex)
        {
            BackEndLog.Log("CommandInputPort_ExecuteSingle_workflow_fallback",
                new { command = commandText, error = ex.Message });
            return null;
        }
    }

    public void ExecuteAll()
    {
        // F4.3：作用域 Failed 时短路（策略位开时）
        if (TryFailFastStop())
            return;

        _coordinator.Update();
    }

    /// <summary>异步执行全部 pending 命令：对异步命令系统以 await 执行，不阻塞调用线程。</summary>
    public Task ExecuteAllAsync()
    {
        // F4.3：作用域 Failed 时短路（策略位开时）
        if (TryFailFastStop())
            return Task.CompletedTask;

        return _coordinator.UpdateAsync();
    }

    /// <summary>
    /// F4.3（fail-stop·策略位）：存在 Failed 作用域时记录首个根因并短路脚本执行，返回 true。
    /// 仅当 failFastOnScopeError 开启、且 IStateCenter 已装配（无 StateCenter 的独立命令环境不短路）。
    /// </summary>
    private bool TryFailFastStop()
    {
        if (!_failFastOnScopeError)
            return false;

        var center = AppServiceProvider.Resolve<Share.StateManagement.IStateCenter>();
        if (center == null)
            return false;

        var failed = center.Scopes.GetScopes()
            .Where(s => center.Scopes.GetState(s)?.State == Share.StateManagement.ScopeState.Failed)
            .ToList();
        if (failed.Count == 0)
            return false;

        BackEndLog.Log("CommandInputPort_fail_fast_on_scope_error",
            new
            {
                failedScopes = string.Join(",", failed),
                note = "作用域渲染失败，脚本提前终止（failFastOnScopeError）"
            });
        return true;
    }
}