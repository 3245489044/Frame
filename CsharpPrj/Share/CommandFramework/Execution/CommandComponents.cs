using ECS.Core;

namespace CommandFramework.Execution.Commands;

public enum CommandState
{
    Pending,
    Executing,
    Success,
    Failed,
    ParseError
}

public static class CommandStateExtensions
{
    public static string ToDisplayText(this CommandState state) => state switch
    {
        CommandState.Pending => "Pending",
        CommandState.Executing => "Executing",
        CommandState.Success => "Success",
        CommandState.Failed => "Failed",
        CommandState.ParseError => "ParseErr",
        _ => "Unknown"
    };
}

public static class CommandEntityExtensions
{
    public static List<Entity> GetPendingCommandEntities(this EntityManager entityManager)
    {
        return entityManager.GetEntities<CommandStatusComponent>()
            .Where(e =>
            {
                // 防御：快照内实体可能已被外部销毁（如作用域销毁误伤/ID 复用），
                // 用 TryGetComponent 跳过而非 GetComponent 抛异常中断整条管线。
                return entityManager.TryGetComponent<CommandStatusComponent>(e, out var status)
                    && status.State == CommandState.Pending;
            })
            .ToList();
    }

    public static bool HasPendingCommandEntities(this EntityManager entityManager)
    {
        return entityManager.GetEntities<CommandStatusComponent>()
            .Any(e =>
            {
                return entityManager.TryGetComponent<CommandStatusComponent>(e, out var status)
                    && status.State == CommandState.Pending;
            });
    }

    /// <summary>
    /// 公共实体装配：创建 Entity + CommandMetaComponent + CommandStatusComponent(Pending)。
    /// 收敛 LoadMetaCommands / ParseAndCreateEntity 的重复组件装配模板。
    /// </summary>
    public static Entity CreateCommandEntity(
        this EntityManager entityManager,
        string action,
        string? target,
        Dictionary<string, object>? parameters,
        string rawCommand,
        int index)
    {
        var entity = entityManager.CreateEntity();
        entityManager.AddComponent(entity, new CommandMetaComponent
        {
            Action = action ?? "",
            Target = target,
            Parameters = parameters,
            RawCommand = rawCommand,
            Index = index
        });
        entityManager.AddComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Pending
        });
        return entity;
    }

    /// <summary>
    /// 公共解析错误实体装配：创建 Entity + CommandMetaComponent(Action="") + CommandStatusComponent(ParseError)。
    /// </summary>
    public static Entity CreateParseErrorEntity(
        this EntityManager entityManager,
        string rawCommand,
        int index,
        string errorMessage)
    {
        var entity = entityManager.CreateEntity();
        entityManager.AddComponent(entity, new CommandMetaComponent
        {
            Action = "",
            RawCommand = rawCommand,
            Index = index
        });
        entityManager.AddComponent(entity, new CommandStatusComponent
        {
            State = CommandState.ParseError,
            ErrorMessage = errorMessage
        });
        return entity;
    }
}

public struct CommandMetaComponent : IComponent
{
    public string Action;

    public string? Target;

    public Dictionary<string, object>? Parameters;

    public string RawCommand;

    public int Index;
}

public struct CommandStatusComponent : IComponent
{
    public CommandState State;

    public string? ErrorMessage;

    public long DurationMs;
}

/// <summary>命令结果结构码（P3·结构化结果契约）：机检原因码，替代「无异常即成功」的二元语义。</summary>
public enum CommandOutcome
{
    /// <summary>命令成功执行。</summary>
    Ok,

    /// <summary>目标控件/实体未找到。</summary>
    TargetNotFound,

    /// <summary>目标控件存在但未挂载/未渲染到活动作用域。</summary>
    TargetNotMounted,

    /// <summary>事件（click）未送达绑定链（实体缺失或未匹配绑定）。</summary>
    EventNotDispatched,

    /// <summary>目标函数执行失败。</summary>
    FunctionFailed,

    /// <summary>未知命令。</summary>
    UnknownCommand,

    /// <summary>命令解析错误。</summary>
    ParseError,

    /// <summary>其他/未分类失败。</summary>
    Error
}

public struct CommandResultComponent : IComponent
{
    public string? ResultValue;

    public DateTime CompletedAt;

    public CommandOutcome Outcome;

    public string? Target;
}

public struct CloseRequestComponent : IComponent
{
    public string? WindowTitle;
}

public struct ExitRequestComponent : IComponent
{
    public bool Handled;
}
