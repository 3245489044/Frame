﻿
using System.Windows.Forms;
using ECS.Core;
using BackEndDriven.Abstractions;
using BackEndDriven.Models;
using BasicMethod.ContextFrame.Core;
using TConfig.ContextFrame.Abstractions;
using TConfig.Abstractions;
using TConfig;
using CommandFramework.Models;
using CommandFramework.Parsing;
using BatchContextData = Modules.DiagnosticContext.BatchContextData;
using CommandLineResult = TConfig.CommandLineResult;
using TSPoolPipelineContext = Share.TaskStatusPool.Abstractions.IPipelineContext;

namespace BackEndDriven.CommandExecution.Context;

public class CommandPipelineContext : CommandContext, IBackEndPipelineContext
{

    private readonly IContext _contextFrame;

    internal IContext ContextFrame => _contextFrame;

    public BatchContextData BatchData { get; set; } = new();

    // ── 命令执行管线运行状态（原 CommandRunContext 并入，消除双轨） ──
    /// <summary>待执行的命令实体集合（由 CommandInputPort 装载的 pending 实体）。</summary>
    public IReadOnlyList<Entity> Entities { get; init; } = Array.Empty<Entity>();

    /// <summary>是否请求停止后续命令（halt/forcehalt 语义 → 各命令 Step 经 CanExecute 短路跳过）。</summary>
    public bool StopRequested { get; set; }
    // CurrentIndex 继承自 CommandContext（当前命令索引）。


    public new string? Mode
    {
        get => base.Mode;
        set => base.Mode = value ?? "CLI";
    }

    public string? BatchId
    {
        get => BatchData.BatchId;
        set => BatchData.BatchId = value;
    }

    public string BatchName
    {
        get => BatchData.BatchName;
        set => BatchData.BatchName = value;
    }

    public string Source
    {
        get => BatchData.Source;
        set => BatchData.Source = value;
    }

    public string? Description
    {
        get => BatchData.Description;
        set => BatchData.Description = value;
    }

    public string? WorkflowId
    {
        get => BatchData.WorkflowId;
        set => BatchData.WorkflowId = value;
    }

    public string? ProjectRoot
    {
        get => BatchData.ProjectRoot;
        set => BatchData.ProjectRoot = value;
    }

    public bool IsPipeMode
    {
        get => BatchData.IsPipeMode;
        set => BatchData.IsPipeMode = value;
    }

    public string? PipeName
    {
        get => BatchData.PipeName;
        set => BatchData.PipeName = value;
    }



    public List<global::Modules.Parsing.Models.MetaCommand>? ScriptCommands { get; set; }


    public global::Modules.Parsing.Models.MetaVerb ParsedVerb
    {
        get
        {
            if (ParsedCommand?.Verb != null
                && Enum.TryParse<global::Modules.Parsing.Models.MetaVerb>(ParsedCommand.Verb, ignoreCase: true, out var verb))
                return verb;
            throw new InvalidOperationException(
                $"Cannot parse Verb '{ParsedCommand?.Verb}' to MetaVerb. " +
                "Valid values: Do, Get, Look, Wait, Assert");
        }
        set
        {
            EnsureParsedCommand();
            ParsedCommand.Verb = value.ToString().ToLower();
        }
    }

    private void EnsureParsedCommand() => ParsedCommand ??= new ParsedCommand();

    public string? ParsedAction
    {
        get => ParsedCommand?.Action;
        set { EnsureParsedCommand(); ParsedCommand.Action = value; }
    }

    public string? ParsedTarget
    {
        get => ParsedCommand?.Target;
        set { EnsureParsedCommand(); ParsedCommand.Target = value; }
    }

    public Dictionary<string, object>? ParsedParameters
    {
        get => ParsedCommand?.Parameters;
        set { EnsureParsedCommand(); ParsedCommand.Parameters = value; }
    }

    public string? NormalizedCommand
    {
        get => ParsedCommand?.NormalizedCommand;
        set { EnsureParsedCommand(); ParsedCommand.NormalizedCommand = value; }
    }

    public bool ParseSuccess
    {
        get => ParsedCommand?.IsSuccess ?? false;
        set { EnsureParsedCommand(); ParsedCommand.IsSuccess = value; }
    }

    public string? ParseErrorMessage
    {
        get => ParsedCommand?.ErrorMessage;
        set { EnsureParsedCommand(); ParsedCommand.ErrorMessage = value; }
    }

    public bool IsNonEventCommand
    {
        get => ParsedCommand?.IsNonEventCommand ?? false;
        set { EnsureParsedCommand(); ParsedCommand.IsNonEventCommand = value; }
    }



    public int CollectedLogCount { get; set; }

    public int SkipCount { get; set; }

    public List<CommandLineResult> CommandResults { get; set; } = new();


    public ExecutionOptions? Options
    {
        get => _contextFrame.GetData<ExecutionOptions>();
        set => _contextFrame.SetData(value);
    }

    public IEventBindingResolver? BindingResolver
    {
        get => _contextFrame.GetData<IEventBindingResolver>();
        set => _contextFrame.SetData(value);
    }

    public IFunctionExecutor? FunctionExecutor
    {
        get => _contextFrame.GetData<IFunctionExecutor>();
        set => _contextFrame.SetData(value);
    }

    public Form? TargetForm
    {
        get => _contextFrame.GetData<Form>();
        set => _contextFrame.SetData(value);
    }

    public object? UICommandManager
    {
        get => _contextFrame.GetData<object>(ContextKeys.UICommandManager);
        set => _contextFrame.SetData(value, ContextKeys.UICommandManager);
    }

    public new IDictionary<string, object>? LoadedConfigs
    {
        get => _contextFrame.GetData<IDictionary<string, object>>();
        set => _contextFrame.SetData(value);
    }

    public bool SideloadFailed
    {
        get => _contextFrame.GetData<bool>(ContextKeys.SideloadFailed);
        set => _contextFrame.SetData(value, ContextKeys.SideloadFailed);
    }


    public CommandPipelineContext() : base()
    {
        _contextFrame = new PipelineContextBase(id: Id);
    }

    public static bool IsNonEventCommandAction(string? action)
        => global::CommandFramework.Libraries.NonEventCommands.IsNonEventCommand(action);

    public static string? ResolveScriptPath(string scriptPath, string? projectRoot)
    {
        var fullPath = scriptPath;
        if (!File.Exists(fullPath) && !string.IsNullOrWhiteSpace(projectRoot))
        {
            fullPath = Path.Combine(projectRoot, scriptPath);
        }

        if (!File.Exists(fullPath))
            return null;

        return Path.GetFullPath(fullPath);
    }


    string? TSPoolPipelineContext.Mode
    {
        get => Mode;
        set => Mode = value ?? "CLI";
    }

    string? TSPoolPipelineContext.BatchId
    {
        get => BatchId;
        set => BatchId = value ?? string.Empty;
    }

    private bool IsCancelledFromFrame
    {
        get => _contextFrame.GetData<bool>(ContextKeys.IsCancelled);
        set
        {
            _contextFrame.SetData(value, ContextKeys.IsCancelled);
            if (value)
                Cancel();
        }
    }

    bool TSPoolPipelineContext.IsCancelled
    {
        get => IsCancelledFromFrame;
        set => IsCancelledFromFrame = value;
    }

    bool TSPoolPipelineContext.IsExecuting { get; set; }

    Dictionary<string, object?> TSPoolPipelineContext.SharedData
    {
        get => (Dictionary<string, object?>)SharedData;
    }

    List<CommandLineResult> IBackEndPipelineContext.CommandResults
    {
        get => CommandResults;
    }

    string? TSPoolPipelineContext.ConfigPath
    {
        get => Options?.ConfigPath;
        set { if (Options != null) Options.ConfigPath = value; }
    }

    string? TSPoolPipelineContext.CommandFile
    {
        get => ScriptPath;
        set => ScriptPath = value;
    }

    bool TSPoolPipelineContext.AutoClose
    {
        get => Options?.AutoClose ?? false;
        set { if (Options != null) Options.AutoClose = value; }
    }

    void IBackEndPipelineContext.RecordCommandResult(CommandLineResult result)
    {
        CommandResults.Add(result);
        if (result.Success)
            SuccessCount++;
        else
            FailCount++;
    }

    string TSPoolPipelineContext.GetSummary()
        => $"Total={TotalCommands}, Success={SuccessCount}, Fail={FailCount}, Skip={SkipCount}";

}

public static class EventBindingResolverExtensions
{
    public static Func<string, string, (string? FunctionName, List<string>? Inputs, string? Output)> ToFindBindingFunc(
        this IEventBindingResolver resolver)
    {
        return (controlName, eventType) =>
        {
            var binding = resolver.FindEventBinding(controlName, eventType);
            if (binding == null)
                return (null, null, null);
            return (binding.FunctionName, null, binding.Output);
        };
    }
}
