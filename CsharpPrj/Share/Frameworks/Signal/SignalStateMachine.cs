using System;
using System.Collections.Generic;

namespace Share.Signal;

/// <summary>
/// 信号状态机：唯一迁移判定入口。合法边唯一权威 = 声明面；未配置时使用 fail-safe 默认边集。
/// <para>生命周期是一对多图，故使用边集而非单值转移表。</para>
/// </summary>
public static class SignalStateMachine
{
    private static readonly HashSet<string> DefaultEdges = new(StringComparer.Ordinal)
    {
        "created>accepted",
        "accepted>queued",
        "accepted>dispatched",
        "accepted>failed",
        "queued>dispatched",
        "queued>expired",
        "queued>cancelled",
        "dispatched>claimed",
        "dispatched>expired",
        "dispatched>failed",
        "dispatched>completed",
        "dispatched>cancelled",
        "claimed>running",
        "claimed>cancelled",
        "claimed>failed",
        "running>completed",
        "running>failed",
        "running>cancelled",
        "failed>queued",
        "failed>deadletter",
    };

    private static volatile HashSet<string> _edges = DefaultEdges;

    /// <summary>配置声明面边集（null/空 ⇒ 恢复默认边集）。</summary>
    public static void Configure(IReadOnlyList<SignalLifecycleEdge>? edges)
    {
        if (edges is not { Count: > 0 })
        {
            _edges = DefaultEdges;
            return;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges) set.Add($"{edge.From}>{edge.To}");
        _edges = set;
    }

    /// <summary>是否允许 from → to（含自身 = 保持）。</summary>
    public static bool CanTransition(SignalState from, SignalState to)
    {
        if (from == to) return true;
        return _edges.Contains($"{SignalStateNames.Of(from)}>{SignalStateNames.Of(to)}");
    }

    /// <summary>是否允许按声明词迁移。</summary>
    public static bool CanTransition(string? from, string? to)
    {
        var f = SignalStateNames.Parse(from);
        var t = SignalStateNames.Parse(to);
        return f is { } fs && t is { } ts && CanTransition(fs, ts);
    }

    /// <summary>单后继（仅当该状态在声明中只有一个后继；多后继 ⇒ null）。</summary>
    public static SignalState? Next(SignalState from)
    {
        var prefix = SignalStateNames.Of(from) + ">";
        string? found = null;
        foreach (var edge in _edges)
        {
            if (!edge.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (found is not null) return null;
            found = edge[prefix.Length..];
        }
        return found is null ? null : SignalStateNames.Parse(found);
    }

    /// <summary>终态判定（Failed 可重试，不算最终态）。</summary>
    public static bool IsTerminal(SignalState state) => SignalStateNames.IsTerminal(state);

    /// <summary>在途判定。</summary>
    public static bool IsInFlight(SignalState state) => SignalStateNames.IsInFlight(state);
}
