
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Parsing;
using BP = Modules.Pipeline;


namespace CommandFramework.Models;


public class CommandContext : BP.PipelineContext, IPipelineContext, TConfig.IExecutionContext
{

    public string? CurrentCommand { get; set; }

    public int CurrentIndex { get; set; } = -1;

    public int CurrentCommandLineNumber { get; set; }

    public ParsedCommand? ParsedCommand { get; set; }


    public string? BoundFunctionName { get; set; }

    public object? FunctionResultValue { get; set; }

    public TimeSpan FunctionExecutionTime { get; set; }



    public string? ScriptPath { get; set; }

    public bool ScriptLoadSuccess { get; set; }

    public string? ScriptLoadErrorMessage { get; set; }

    public string[]? CommandLines { get; set; }

    public string? CommandFilePath { get; set; }


    // R-2（8.6）：CLI 命令期参数回填的临时字典（命令级生命周期，set/get/click 命令读写）。
    // 与 Share.StateManagement.VirtualStore（通用状态存储权威）用途不同：本字段承载「单次命令管线」内
    // 参数回填的共享字典，不承担跨会话持久化。因 TConfig 分层约束（TConfig 不引用 StateManagement）
    // 类型保持 Dictionary，不再以 new 隐藏方式在子类（CommandPipelineContext）重复定义第二份存储。
    public Dictionary<string, string?>? VirtualStore { get; set; }

    public IDictionary<string, object>? LoadedConfigs { get; set; }


    public int TotalCommands { get; set; }

    public int SuccessCount { get; set; }

    public int FailCount { get; set; }


    public CommandExecutionReport? ExecutionReport { get; set; }



    public bool IsGuiMode => Mode.Equals("GUI", StringComparison.OrdinalIgnoreCase);

    public bool IsCliMode => Mode.Equals("CLI", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] ApplicableStages =
        { "ScriptLoad", "ParseCommand", "ExecuteCommand" };


    /// <summary>
    /// 返回本次管线已应用的应用阶段名。注意与 <see cref="GetFirstFailure"/> 的差异（B2 澄清）：
    /// ParsedCommand==null 时返回空数组（供 ResultCollectStage 汇总用）；而 GetFirstFailure 直接遍历
    /// ApplicableStages 并仅判显式 set 的 stage，从而不漏判 ParsedCommand==null 时的脚本加载失败（B1 修复）。
    /// 若未来需二者语义严格一致，应让 ParseCommand 阶段在解析前显式 SetStageResult，而非依赖 ParsedCommand 判据。
    /// </summary>
    internal string[] GetApplicableStages()
    {
        if (ParsedCommand == null)
            return Array.Empty<string>();

        return ApplicableStages;
    }

    private (string? StageName, string? ErrorMessage) GetFirstFailure()
    {
        // 直接遍历 ApplicableStages（含 ScriptLoad），仅判定显式设置过的 stage；
        // 不依赖 GetApplicableStages——ParsedCommand==null 时其返回空数组，会漏判脚本加载失败（B1）。
        foreach (var stageName in ApplicableStages)
        {
            if (!IsStageExplicitlySet(stageName))
                continue;

            if (!GetStageSuccess(stageName))
            {
                return (stageName, GetStageError(stageName)
                    ?? $"{stageName} failed (no detailed error message)");
            }
        }
        return (null, null);
    }

    public override bool HasError
    {
        get
        {
            var (stageName, _) = GetFirstFailure();
            return stageName != null;
        }
    }

    public string? GetErrorMessage()
    {
        var (_, errorMessage) = GetFirstFailure();
        return errorMessage;
    }



    public static implicit operator CommandExecutionResult<object?>(CommandContext context)
    {
        if (context.HasError)
        {
            return CommandExecutionResult<object?>.Failure(
                context.GetErrorMessage() ?? "Unknown error",
                context.CurrentCommand ?? "");
        }

        return CommandExecutionResult<object?>.Success(
            context.FunctionResultValue,
            context.CurrentCommand ?? "")
            .WithExecutionTime(context.FunctionExecutionTime);
    }


    string TConfig.IExecutionContext.ExecutionId => Id;

    string TConfig.IExecutionContext.WorkflowId =>
        SharedData.TryGetValue("__WorkflowId", out var v) && v is string s ? s : string.Empty;

    string TConfig.IExecutionContext.NodeKey =>
        SharedData.TryGetValue("__NodeKey", out var v) && v is string s ? s : string.Empty;

    IReadOnlyDictionary<string, object> TConfig.IExecutionContext.Parameters
    {
        get
        {
            var parameters = new Dictionary<string, object>();
            if (ParsedCommand?.Parameters != null)
            {
                foreach (var kvp in ParsedCommand.Parameters)
                    parameters[kvp.Key] = kvp.Value;
            }
            return parameters;
        }
    }

    IDictionary<string, object> TConfig.IExecutionContext.Items => _itemsAdapter ??= new NonNullDictionaryAdapter(SharedData);

    private IDictionary<string, object>? _itemsAdapter;

    CancellationToken TConfig.IExecutionContext.CancellationToken
    {
        get => CancellationToken;
        set => CancellationToken = value;
    }

    IServiceProvider? TConfig.IExecutionContext.ServiceProvider
    {
        get => SharedData.TryGetValue("__ServiceProvider", out var v) && v is IServiceProvider sp ? sp : null;
        set { if (value != null) SharedData["__ServiceProvider"] = value; }
    }

    T TConfig.IExecutionContext.GetParameter<T>(string key)
    {
        if (ParsedCommand?.Parameters != null &&
            ParsedCommand.Parameters.TryGetValue(key, out var val))
        {
            if (val is T typed) return typed;
            try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
        }
        throw new KeyNotFoundException($"参数未找到: {key}");
    }

    T TConfig.IExecutionContext.GetParameter<T>(int index)
    {
        if (ParsedCommand?.Parameters != null && index >= 0 && index < ParsedCommand.Parameters.Count)
        {
            var val = ParsedCommand.Parameters.Values.ElementAt(index);
            if (val is T typed) return typed;
            try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
        }
        throw new KeyNotFoundException($"参数索引未找到: {index}");
    }

    bool TConfig.IExecutionContext.TryGetParameter<T>(string key, out T value)
    {
        if (ParsedCommand?.Parameters != null &&
            ParsedCommand.Parameters.TryGetValue(key, out var val))
        {
            if (val is T typed) { value = typed; return true; }
            try { value = (T)Convert.ChangeType(val, typeof(T)); return true; } catch { }
        }
        value = default!;
        return false;
    }

    T TConfig.IExecutionContext.GetData<T>(string key)
    {
        if (SharedData.TryGetValue(key, out var val))
        {
            if (val is T typed) return typed;
            try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
        }
        throw new KeyNotFoundException($"数据项未找到: {key}");
    }

    bool TConfig.IExecutionContext.TryGetData<T>(string key, out T value)
    {
        if (SharedData.TryGetValue(key, out var val))
        {
            if (val is T typed) { value = typed; return true; }
            try { value = (T)Convert.ChangeType(val, typeof(T)); return true; } catch { }
        }
        value = default!;
        return false;
    }

    void TConfig.IExecutionContext.SetData<T>(string key, T value)
    {
        SharedData[key] = value;
    }

    TConfig.IExecutionContext TConfig.IExecutionContext.CreateSubContext(string? nodeKey)
    {
        return new CommandContext
        {
            CurrentCommand = CurrentCommand,
            CurrentIndex = CurrentIndex,
            CurrentCommandLineNumber = CurrentCommandLineNumber,
            ParsedCommand = ParsedCommand,
            BoundFunctionName = BoundFunctionName,
            FunctionResultValue = FunctionResultValue,
            FunctionExecutionTime = FunctionExecutionTime,
            ScriptPath = ScriptPath,
            ScriptLoadSuccess = ScriptLoadSuccess,
            ScriptLoadErrorMessage = ScriptLoadErrorMessage,
            CommandLines = CommandLines,
            CommandFilePath = CommandFilePath,
            VirtualStore = VirtualStore,
            LoadedConfigs = LoadedConfigs,
            TotalCommands = TotalCommands,
            SuccessCount = SuccessCount,
            FailCount = FailCount,
            ExecutionReport = ExecutionReport,
            Mode = Mode
        };
    }

    public override void Reset()
    {
        base.Reset();

        CurrentCommand = null;
        CurrentCommandLineNumber = 0;
        ParsedCommand = null;

        FunctionResultValue = null;
        FunctionExecutionTime = TimeSpan.Zero;

        ScriptPath = null;
        ScriptLoadSuccess = false;
        ScriptLoadErrorMessage = null;
        CommandLines = null;
        CommandFilePath = null;

    }

}
