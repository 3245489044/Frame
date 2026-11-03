namespace Share.TaskStatusPool.TaskPool;

/// <summary>Turn 生命周期观测钩子：成员任务归属唯一登记（宿主实现，通用编排只调端口）。</summary>
public interface ITaskTurnObserver
{
    void AttachTask(string historyId, string taskKey);
}

/// <summary>种子策略钩子：可执行条目种子非空判据（宿主实现，通用编排只调端口）。</summary>
public interface ITaskSeedPolicy
{
    /// <summary>回退开关：false ⇒ 不强制非空（保持旧宿主行为）。</summary>
    bool RejectEmptyOnEnqueue { get; }

    /// <summary>种子校验：空种子 ⇒ 抛（入口判据，不落库不激活）。</summary>
    void RequireNonEmpty(string? input);
}

/// <summary>诊断钩子：通用编排的可见化出口（宿主绑定自有日志）。</summary>
public interface ITaskDiagnostics
{
    void Log(string key, object? payload);
}

/// <summary>空实现：未装配宿主时保持可运行（不静默丢失：由调用方显式选择）。</summary>
public sealed class NullTaskTurnObserver : ITaskTurnObserver
{
    public static NullTaskTurnObserver Instance { get; } = new();

    public void AttachTask(string historyId, string taskKey)
    {
    }
}

/// <summary>空实现：不强制非空种子（等价旧默认）。</summary>
public sealed class LenientTaskSeedPolicy : ITaskSeedPolicy
{
    public static LenientTaskSeedPolicy Instance { get; } = new();

    public bool RejectEmptyOnEnqueue => false;

    public void RequireNonEmpty(string? input)
    {
    }
}

/// <summary>空实现：不做诊断输出。</summary>
public sealed class NullTaskDiagnostics : ITaskDiagnostics
{
    public static NullTaskDiagnostics Instance { get; } = new();

    public void Log(string key, object? payload)
    {
    }
}
