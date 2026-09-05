
using MediatR;

namespace Container.Abstractions;

#region 执行模式与状态枚举

public enum ExecutionMode
{
    Cli = 0,

    Gui = 1,

    Test = 2,

    Event = 3
}

public enum ContainerStatus
{
    Idle = 0,

    Running = 1,

    Completed = 2,

    Failed = 3,

    Cancelled = 4
}

#endregion

#region 执行请求与结果

public record ExecutionRequest(
    string CommandText,
    IReadOnlyDictionary<string, object>? Parameters = null,
    string? Source = null,
    string? ScriptPath = null,
    ExecutionMode Mode = ExecutionMode.Cli,
    IRuntimeContext? RuntimeContext = null
) : IRequest<ExecutionResult>;

public record ExecutionResult(
    string CommandText,
    bool Success,
    string? OutputValue,
    string? ErrorMessage,
    TimeSpan ExecutionTime,
    string? BoundFunctionName = null
)
{
    public static ExecutionResult Ok(string cmd, string? value = null)
        => new(cmd, true, value, null, TimeSpan.Zero);

    public static ExecutionResult Fail(string cmd, string error)
        => new(cmd, false, null, error, TimeSpan.Zero);
}

#endregion

#region 引擎请求与结果

public record EngineRequest(
    string Mode,
    string? ConfigPath = null,
    IReadOnlyDictionary<string, object>? GlobalParameters = null
);

public record EngineExecutionResult(
    bool Success,
    TimeSpan TotalExecutionTime,
    string? ErrorMessage = null
);

#endregion

#region 引擎状态变更事件

public class EngineStateChangedEventArgs : EventArgs
{
    public ContainerStatus OldStatus { get; init; }

    public ContainerStatus NewStatus { get; init; }

    public string? Message { get; init; }
}

#endregion
