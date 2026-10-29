using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务执行器契约（通用，Agent 无关）：把「任务怎么执行」从「任务池怎么装配」里拆出。
/// 框架本体不定义任何具体执行器；宿主按任务字段解析到实现句柄（LLM / 规则 / 显式腿 …）。
/// </summary>
public interface ITaskExecutor<in TItem, TResult>
    where TItem : notnull
{
    /// <summary>执行器名称（诊断/按名解析的唯一身份）。</summary>
    string Name { get; }

    /// <summary>是否可处理该任务（按调度模式/载荷能力匹配）。</summary>
    bool CanExecute(TItem item);

    /// <summary>执行任务（item → TResult）。取消经 ct 传导。</summary>
    Task<TResult> ExecuteAsync(TItem item, string executionId, CancellationToken ct);
}

/// <summary>
/// 暂停感知执行器（通用）：在循环边界调用任务池暂停门（合作式挂起 Running ⇄ Paused）。
/// 由 <see cref="TaskExecutorRegistry{TItem,TResult}.AttachPauseGate"/> 统一注入，宿主不再逐个类型判断。
/// </summary>
public interface IPauseAwareTaskExecutor<in TItem, TResult> : ITaskExecutor<TItem, TResult>
    where TItem : notnull
{
    /// <summary>注入暂停门（taskKey → 若已暂停则阻塞至恢复）。null = no-op。</summary>
    void SetPauseGate(Func<string, CancellationToken, Task>? gate);
}

/// <summary>
/// 持久化感知执行器（通用）：宿主把任务池持久化端口注入执行器（如每轮边界保存运行时快照），
/// 消除「装配层按具体执行器类型特判赋值」的类型硬编码。
/// </summary>
public interface ITaskPoolPersistenceAware<in TPersistence>
    where TPersistence : class
{
    void AttachPersistence(TPersistence persistence);
}

/// <summary>
/// 执行器注册表（通用）：注册 + 按名解析 + 别名 + 暂停门/持久化统一装配。
/// <para>解析语义（与既有按名解析一致）：名称为 Ordinal 精确匹配；别名表把历史腿 Id 指到规范执行器，
/// 别名冲突 / 目标未注册 ⇒ 装配期 fail-fast（禁静默覆盖）。</para>
/// <para>本类型不承载「该用哪个执行器」的策略（显式执行器优先 / 调度模式路由），策略由宿主按
/// <see cref="TryGet"/> 组合（机制下沉、策略留域）。</para>
/// </summary>
public sealed class TaskExecutorRegistry<TItem, TResult>
    where TItem : notnull
{
    private readonly List<ITaskExecutor<TItem, TResult>> _all = new();
    private readonly Dictionary<string, ITaskExecutor<TItem, TResult>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

    /// <summary>已注册执行器（诊断 / 宿主装配视图）。</summary>
    public IReadOnlyList<ITaskExecutor<TItem, TResult>> All => _all;

    public int Count => _all.Count;

    /// <summary>注册执行器（同名重复 ⇒ fail-fast，禁静默覆盖）。</summary>
    public void Register(ITaskExecutor<TItem, TResult> executor, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (string.IsNullOrWhiteSpace(executor.Name))
            throw new ArgumentException("执行器 Name 不能为空（按名解析的唯一身份）。", nameof(executor));
        if (_byName.ContainsKey(executor.Name))
            throw new InvalidOperationException($"执行器名称重复注册：'{executor.Name}'（名称唯一）。");

        _byName.Add(executor.Name, executor);
        _all.Add(executor);

        foreach (var alias in aliases)
            RegisterAlias(alias, executor.Name);
    }

    /// <summary>登记别名（alias → 已注册的规范名）。</summary>
    public void RegisterAlias(string alias, string targetName)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(targetName)) return;
        var key = alias.Trim();
        if (!_byName.ContainsKey(targetName))
            throw new InvalidOperationException($"别名 '{key}' 指向未注册执行器 '{targetName}'（禁悬空别名）。");
        if (_byName.ContainsKey(key))
            throw new InvalidOperationException($"别名 '{key}' 与已注册执行器同名（禁歧义）。");
        if (_aliases.TryGetValue(key, out var existing) && !string.Equals(existing, targetName, StringComparison.Ordinal))
            throw new InvalidOperationException($"别名 '{key}' 重复登记（已指向 '{existing}'）。");

        _aliases[key] = targetName;
    }

    /// <summary>按名解析（先查别名表，再查注册名；空白 / 未命中 ⇒ false）。</summary>
    public bool TryGet(string? name, out ITaskExecutor<TItem, TResult> executor)
    {
        executor = null!;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var key = name.Trim();
        var canonical = _aliases.TryGetValue(key, out var target) ? target : key;
        return _byName.TryGetValue(canonical, out executor!);
    }

    /// <summary>把暂停门注入所有 <see cref="IPauseAwareTaskExecutor{TItem,TResult}"/>（非暂停感知执行器忽略）。</summary>
    public void AttachPauseGate(Func<string, CancellationToken, Task>? gate)
    {
        foreach (var executor in _all)
            if (executor is IPauseAwareTaskExecutor<TItem, TResult> aware)
                aware.SetPauseGate(gate);
    }

    /// <summary>把持久化端口注入所有 <see cref="ITaskPoolPersistenceAware{TPersistence}"/>（其余执行器忽略）。</summary>
    public void AttachPersistence<TPersistence>(TPersistence persistence)
        where TPersistence : class
    {
        ArgumentNullException.ThrowIfNull(persistence);
        foreach (var executor in _all)
            if (executor is ITaskPoolPersistenceAware<TPersistence> aware)
                aware.AttachPersistence(persistence);
    }
}
