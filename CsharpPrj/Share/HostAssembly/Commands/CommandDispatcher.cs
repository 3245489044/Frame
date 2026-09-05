using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Commands;

/// <summary>
/// 表驱动命令分发器 —— 把「action → handler」查表（Resolve(table)，位置即语义）收敛为可复用基座：
/// 按 action（不区分大小写）注册/分发 handler，未识别返回 null（由调用方输出「未知 action」诊断）。
/// 允许动作集（<see cref="Allow"/>）可独立维护，用于对外暴露允许动作白名单。
/// </summary>
/// <typeparam name="TRequest">命令请求体类型（action 由调用方从请求体解析后传入）。</typeparam>
public sealed class CommandDispatcher<TRequest>
{
    private readonly Dictionary<string, AsyncCommandHandler<TRequest>> _handlers;
    private readonly HashSet<string> _allowedActions;

    public CommandDispatcher()
    {
        _handlers = new Dictionary<string, AsyncCommandHandler<TRequest>>(StringComparer.OrdinalIgnoreCase);
        _allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>注册一个 action 的 handler（幂等覆盖）。</summary>
    public CommandDispatcher<TRequest> Register(string action, AsyncCommandHandler<TRequest> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[action.Trim()] = handler;
        return this;
    }

    /// <summary>登记一个允许对外暴露的 action（白名单，供诊断/自省使用）。</summary>
    public CommandDispatcher<TRequest> Allow(string action)
    {
        if (!string.IsNullOrWhiteSpace(action)) _allowedActions.Add(action.Trim());
        return this;
    }

    /// <summary>分发 action 到对应 handler；未注册返回 null。action 前后空白与大小写被规范化。</summary>
    public Task<object?>? TryDispatchAsync(TRequest request, string? action, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var key = action.Trim().ToLowerInvariant();
        if (!_handlers.TryGetValue(key, out var handler)) return null;
        return handler(request, ct);
    }

    /// <summary>已注册的 action 集合。</summary>
    public IReadOnlyCollection<string> RegisteredActions => _handlers.Keys;

    /// <summary>允许对外暴露的 action 白名单。</summary>
    public IReadOnlyCollection<string> AllowedActions => _allowedActions;
}

/// <summary>异步命令 handler 委托。</summary>
public delegate Task<object?> AsyncCommandHandler<in TRequest>(TRequest request, CancellationToken ct);
