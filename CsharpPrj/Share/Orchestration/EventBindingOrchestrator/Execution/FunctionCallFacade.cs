
#nullable enable
using FrameworkAbstractions.WinFormsAbstractions;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Invocation;

namespace Share.EventBindingOrchestrator.Execution;

public enum FunctionCallSource
{
    ButtonClick,
    InitCommand,
    ExecCommand,
    CommandExecutor,
    ExternalPort,
    MatrixSink,
    Other
}

/// <summary>
/// 函数渲染副作用元数据（阶段2.2：替代 FunctionExecutedRenderBridge 的 StartsWith("LoadConfig") 前缀黑魔法）。
/// 由执行门面（FunctionCallFacade）解析并随 FunctionExecuted 事件携带；消费方（渲染桥）据此裁决是否重渲染。
/// </summary>
public enum RenderingAffinity
{
    /// <summary>函数执行不触发 UI 渲染（默认）。</summary>
    None,

    /// <summary>函数内部已完成完整渲染（如 LoadConfig 类 → LoadAndRender），渲染桥不得再次重渲染。</summary>
    SelfRendered,

    /// <summary>函数执行后需重放作用域快照（如填充列表类）。</summary>
    ReRenderScope
}

public enum ExecutionMode
{
    Auto,
    Background,
    FireAndForget,
    Sync
}

public sealed record FunctionCallRequest
{
    public required string FunctionName { get; init; }

    public Dictionary<string, object?>? Parameters { get; init; }

    public FunctionCallSource Source { get; init; } = FunctionCallSource.Other;

    public ExecutionMode Mode { get; init; } = ExecutionMode.Auto;

    public int TimeoutMs { get; init; } = InteractionDefaults.DefaultFunctionTimeoutMs;

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>调用关联 ID（opId）：由发起方创建并贯穿全链，用于归因与观测。</summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>函数渲染副作用元数据（可空：null = 由执行门面按函数名推断，见 FunctionCallFacade.ResolveRenderingAffinity）。</summary>
    public RenderingAffinity? RenderingAffinity { get; init; }
}

/// <summary>
/// 统一函数调用门面（下沉自 BackEndDriven.Gate，单一实现权威——[CR: axioms:Strategy.SingleImplementation@consume]）。
/// 已收敛为 <see cref="InvocationKernel"/> 的外观：执行 + 生命周期 + 分类 + 观测统一由内核承载，
/// 本门面仅做请求翻译与 <see cref="FunctionCallFacadeResult"/>（外部契约见 IEventPlatform）投影。
/// </summary>
public static class FunctionCallFacade
{
    public static async Task<FunctionCallFacadeResult> ExecuteAsync(FunctionCallRequest request)
    {
        PortGuard.Required(request, nameof(request));

        // 阶段2.2：渲染副作用元数据解析（显式声明优先，否则按函数名推断）——随结果携带，供事件/渲染桥消费。
        var affinity = ResolveRenderingAffinity(request);

        var invocation = new InvocationRequest(
            request.FunctionName,
            request.Parameters ?? new Dictionary<string, object?>(),
            ToSource(request.Source),
            // 单一权威计时：预算=请求超时（<=0 不设绝对上限）；空闲窗口关闭（默认）——慢函数不再被全局空闲看门狗误杀。
            new InvocationBudget(AbsoluteMs: request.TimeoutMs > 0 ? request.TimeoutMs : null, IdleMs: null),
            CorrelationId: request.CorrelationId);

        var result = await InvocationKernel.ExecuteAsync(invocation).ConfigureAwait(false);

        return new FunctionCallFacadeResult(
            result.IsSuccess,
            result.IsSuccess ? result.Output?.ToString() : null,
            result.IsSuccess ? null : (result.Error ?? "未知错误"),
            result.ElapsedMs,
            affinity);
    }

    public static FunctionCallFacadeResult ExecuteSyncRequest(FunctionCallRequest request)
    {
        PortGuard.Required(request, nameof(request));

        return RunSyncCore(() => ExecuteAsync(request), -1);
    }

    private static InvocationSource ToSource(FunctionCallSource source) => source switch
    {
        FunctionCallSource.ButtonClick => InvocationSource.ButtonClick,
        FunctionCallSource.ExecCommand => InvocationSource.ExecCommand,
        FunctionCallSource.CommandExecutor => InvocationSource.CommandClick,
        FunctionCallSource.InitCommand => InvocationSource.InitCommand,
        FunctionCallSource.MatrixSink => InvocationSource.MatrixSink,
        FunctionCallSource.ExternalPort => InvocationSource.ExternalPort,
        _ => InvocationSource.Unknown
    };

    /// <summary>
    /// 渲染副作用元数据解析（M5·注册化降级路径，方案 §六 风险表）：显式声明优先；
    /// 否则按显式映射表（函数名 → RenderingAffinity，覆盖 UIToolbox 已注册的加载渲染类函数）解析；兜底 None。
    /// 删除前缀推断（StartsWith("LoadConfig") 语义编码在命名约定；改名/新增函数即破坏抑制行为）。
    /// TODO：迁移至函数注册表元数据（ITrConfigRegistry 扩展 RenderingAffinity，UIToolbox 注册时显式声明）后删除映射表。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, RenderingAffinity> AffinityOverrides =
        new Dictionary<string, RenderingAffinity>(StringComparer.OrdinalIgnoreCase)
        {
            ["LoadConfig"] = RenderingAffinity.SelfRendered,
            ["LoadConfigToTarget"] = RenderingAffinity.SelfRendered,
            ["LoadSidePanel"] = RenderingAffinity.SelfRendered,
            ["BrowseConfigFile"] = RenderingAffinity.SelfRendered,
            ["LoadJsonConfigList"] = RenderingAffinity.ReRenderScope,
        };

    private static RenderingAffinity ResolveRenderingAffinity(FunctionCallRequest request)
    {
        if (request.RenderingAffinity is { } explicitAffinity)
            return explicitAffinity;

        return AffinityOverrides.TryGetValue(request.FunctionName, out var affinity)
            ? affinity
            : RenderingAffinity.None;
    }

    /// <summary>同步等待辅助（下沉自 BackEndDriven.Utilities.AsyncHelper——Share 层不依赖 Operation 层）。</summary>
    private static TResult RunSyncCore<TResult>(Func<Task<TResult>> asyncFunc, int timeoutMilliseconds)
    {
        asyncFunc = PortGuard.Required(asyncFunc, nameof(asyncFunc));

        var task = Task.Run(asyncFunc);
        if (timeoutMilliseconds > 0)
        {
            if (!task.Wait(timeoutMilliseconds))
            {
                throw new TimeoutException(
                    $"异步任务未在 {timeoutMilliseconds}ms 内完成，可能已死锁或挂起。");
            }
        }

        return task.GetAwaiter().GetResult();
    }
}
