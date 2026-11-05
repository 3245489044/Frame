namespace Modules.Governance;

/// <summary>
/// 进程级限制内核单根（组合根装配一次，全局只读面）。
/// 单一实例：热更新只替换策略/端口，不替换内核对象，保证在途作用域与租约引用稳定。
/// </summary>
public static class LimitKernelHost
{
    private static readonly LimitKernel Instance = new(LimitPolicyDocument.Default);

    /// <summary>进程唯一内核（只读端口）。</summary>
    public static ILimitKernel Current => Instance;

    /// <summary>具体内核（装配期配置策略/端口）。</summary>
    public static LimitKernel Kernel => Instance;

    /// <summary>装配期配置：策略 + 观测/终止/估算端口（整文档原子替换）。</summary>
    public static void Configure(
        LimitPolicyDocument policy,
        ILimitObserver? observer = null,
        ILimitTerminationSink? termination = null,
        IUsageEstimator? estimator = null)
    {
        Instance.Configure(policy);
        if (observer is not null) Instance.SetObserver(observer);
        if (termination is not null) Instance.SetTerminationSink(termination);
        if (estimator is not null) Instance.SetEstimator(estimator);
    }

    /// <summary>观测适配器在事件区就绪后挂接（后挂不影响已配置策略）。</summary>
    public static void AttachObserver(ILimitObserver? observer) => Instance.SetObserver(observer);

    public static void AttachTermination(ILimitTerminationSink? sink) => Instance.SetTerminationSink(sink);

    public static void AttachEstimator(IUsageEstimator? estimator) => Instance.SetEstimator(estimator);

    public static bool IsHalted => Instance.IsHalted;

    public static string? HaltReason => Instance.HaltReason;

    public static void Halt(string? reason = null) => Instance.Halt(reason);

    public static void Reset() => Instance.Reset();
}

/// <summary>
/// 作用域链入口（宿主边界开启；AsyncLocal 自动透传，子 scope 裁决逐级上查父链）。
/// <para>作用域边界：进程启动 → Process（内核持有）；会话 → Session；任务入池 → Task；AgentRunner → Run；每轮 → Round。</para>
/// </summary>
public static class LimitScopeChain
{
    /// <summary>开启一个作用域；返回句柄，Dispose 关闭并恢复前值（嵌套安全）。</summary>
    public static IDisposable Enter(LimitScopeKind kind, string id, string? parentScopeId = null)
        => LimitKernelHost.Kernel.Enter(kind, id, parentScopeId);

    /// <summary>当前逻辑执行链上的作用域快照（无则 null）。</summary>
    public static LimitSnapshot? Current => LimitKernelHost.Kernel.CurrentSnapshot;
}
