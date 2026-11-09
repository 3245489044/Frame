using System.Collections.Concurrent;
using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Control;

/// <summary>待审批条目（挂起中的请求）。</summary>
public sealed class PendingEntry
{
    public required string CallId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required string ClientId { get; init; }
    public string? SessionId { get; init; }
    public string? TaskKey { get; init; }
    public string? Model { get; init; }
    public decimal? EstimatedCostUsd { get; init; }
    public string? RuleId { get; init; }
    public string ReasonCode { get; init; } = "";
    public string Fingerprint { get; init; } = "";

    internal TaskCompletionSource<PendingResolution> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingView ToView() => new(
        CallId, StartedAt, ClientId, SessionId, TaskKey, Model,
        EstimatedCostUsd, RuleId, ReasonCode, Fingerprint);
}

/// <summary>挂起请求的裁决结果。</summary>
public sealed record PendingResolution(
    GateDecision Decision,
    DecidedBy DecidedBy,
    string? ReasonCode,
    string? Reason)
{
    public bool IsAllow => Decision == GateDecision.Allow;
}

/// <summary>待审批队列（上限 maxPending；溢出即拒绝，不排队）。</summary>
public sealed class PendingRegistry
{
    private readonly ConcurrentDictionary<string, PendingEntry> _items = new(StringComparer.Ordinal);
    private readonly int _maxPending;

    public PendingRegistry(int maxPending)
    {
        _maxPending = Math.Max(1, maxPending);
    }

    public int Count => _items.Count;
    public int MaxPending => _maxPending;

    /// <summary>登记挂起项；队列已满 ⇒ false（由调用方转 Deny 溢出）。</summary>
    public bool TryAdd(PendingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_items.Count >= _maxPending)
        {
            return false;
        }

        return _items.TryAdd(entry.CallId, entry);
    }

    public PendingEntry? Get(string? callId)
        => string.IsNullOrWhiteSpace(callId) ? null : _items.TryGetValue(callId!, out var entry) ? entry : null;

    /// <summary>裁决挂起项；不存在/已裁决 ⇒ false。</summary>
    public bool TryResolve(string callId, GateDecision decision, DecidedBy decidedBy, string? reasonCode, string? reason)
    {
        if (string.IsNullOrWhiteSpace(callId) || !_items.TryRemove(callId, out var entry))
        {
            return false;
        }

        return entry.Completion.TrySetResult(new PendingResolution(decision, decidedBy, reasonCode, reason));
    }

    public IReadOnlyList<PendingEntry> Snapshot()
        => _items.Values.OrderBy(p => p.StartedAt).ToList();

    public void Clear()
    {
        foreach (var entry in _items.Values)
        {
            entry.Completion.TrySetResult(new PendingResolution(
                GateDecision.Deny, DecidedBy.Timeout, ReasonCodes.DenyTimeout, "门已停止"));
        }

        _items.Clear();
    }
}
