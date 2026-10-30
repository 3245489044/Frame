#nullable enable

using System;
using System.Collections.Generic;

namespace Strategy.Refresh.Core;

/// <summary>
/// 命令 kind → 优先级注册面（宿主/插件注册领域命令词；框架不硬编码任何领域命令名）。
/// <para>未注册的 kind ⇒ <see cref="UiCommandPriority.Interactive"/>（未知命令不得被后台合并/延迟）。</para>
/// </summary>
public interface ICommandPriorityRegistry
{
    void Register(string kind, UiCommandPriority priority);

    UiCommandPriority Resolve(string kind);
}

/// <summary>注册表默认实现（大小写不敏感；重复注册后者覆盖）。</summary>
public sealed class CommandPriorityRegistry : ICommandPriorityRegistry
{
    private readonly Dictionary<string, UiCommandPriority> _map = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string kind, UiCommandPriority priority)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("kind 不能为空", nameof(kind));
        _map[kind] = priority;
    }

    public UiCommandPriority Resolve(string kind)
        => kind is not null && _map.TryGetValue(kind, out var priority)
            ? priority
            : UiCommandPriority.Interactive;
}

/// <summary>注册便利扩展（宿主只需字符串命令词，无需引用 UI 优先级枚举所在程序集）。</summary>
public static class CommandPriorityRegistryExtensions
{
    /// <summary>批量注册为交互命令（未知命令同样按交互处理，此处仅显式登记以形成可审计清单）。</summary>
    public static void RegisterInteractive(this ICommandPriorityRegistry registry, params string[] kinds)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (kinds is null) return;
        foreach (var kind in kinds)
        {
            if (!string.IsNullOrWhiteSpace(kind)) registry.Register(kind, UiCommandPriority.Interactive);
        }
    }
}

/// <summary>进程级默认注册表（宿主装配期注册领域命令词；仅登记通用刷新/流类命令）。</summary>
public static class DefaultCommandPriorityRegistry
{
    public static ICommandPriorityRegistry Instance { get; } = Create();

    private static ICommandPriorityRegistry Create()
    {
        var registry = new CommandPriorityRegistry();
        registry.Register("refresh", UiCommandPriority.Refresh);
        registry.Register("tick", UiCommandPriority.Background);
        registry.Register("update", UiCommandPriority.Background);
        registry.Register("message", UiCommandPriority.Background);
        registry.Register("stream", UiCommandPriority.Background);
        return registry;
    }
}
