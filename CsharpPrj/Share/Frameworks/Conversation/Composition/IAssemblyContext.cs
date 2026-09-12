namespace Share.Composition;

/// <summary>
/// 装配上下文 —— 类型化输入（替代散落的裸参数）。
/// 内核只依赖本最小面；领域侧扩展字段（Goal/Constraints/ExecResult 等）留在各消费方的具体上下文类型上。
/// </summary>
public interface IAssemblyContext
{
    /// <summary>任务键（缓存主键 / 匹配上下文）。</summary>
    string TaskKey { get; }

    /// <summary>作用域键（缺省回退 <see cref="TaskKey"/>，由消费方归一）。</summary>
    string ScopeKey { get; }

    /// <summary>是否首步（空结果/缺省 → 首步，由调用方判定）。</summary>
    bool IsFirstStep { get; }
}

/// <summary>
/// 装配上下文默认实现 —— 供内核单测与轻量适配使用（领域侧可自行实现 <see cref="IAssemblyContext"/>）。
/// 命名刻意避开领域侧常见名（如 Agent 的 <c>AssemblyContext</c>），使 <c>using Share.Composition;</c>
/// 与领域命名空间并存时不产生类型歧义。
/// 不可变：作用域键归一（null/空 → 回退 TaskKey）在构造期一次完成。
/// </summary>
public sealed class CompositionContext : IAssemblyContext
{
    /// <param name="taskKey">任务键（null → 空串）。</param>
    /// <param name="scopeKey">作用域键（null/空 → 回退 taskKey）。</param>
    /// <param name="isFirstStep">是否首步。</param>
    public CompositionContext(string? taskKey, string? scopeKey = null, bool isFirstStep = false)
    {
        TaskKey = taskKey ?? string.Empty;
        ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? TaskKey : scopeKey!;
        IsFirstStep = isFirstStep;
    }

    /// <inheritdoc />
    public string TaskKey { get; }

    /// <inheritdoc />
    public string ScopeKey { get; }

    /// <inheritdoc />
    public bool IsFirstStep { get; }
}
