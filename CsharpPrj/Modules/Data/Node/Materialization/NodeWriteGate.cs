using Modules.Node.Diagnostics;

namespace Modules.Node.Materialization;

/// <summary>同 target 平行写冲突（fail-fast，不排队）。</summary>
public sealed class NodeWriteConflictException : InvalidOperationException
{
    public NodeWriteConflictException(string target, TimeSpan waited)
        : base($"写门冲突：target '{target}' 已被占用（有界等待 {waited.TotalMilliseconds:F0}ms 未取得，禁止排队）")
    {
    }
}

/// <summary>
/// 单写门（N3 的四要素）：同 target 平行写 <b>fail-fast</b>（抛异常，不排队）+ 同 target 串行 + 有界等待 +
/// 冲突可见化。target 语义由调用方给定（如 <c>{carrierId}|{placementKey}</c>），
/// 与库级写通道（SqliteWriteChannel / 有界写门）关系为「节点级 ⊆ 库级」，二者判据不同（治 P16 门缝）。
/// 门条目按引用计数回收（无在途写者即移除），长跑宿主不随 target 数无界增长。
/// </summary>
public sealed class NodeWriteGate
{
    private sealed class GateEntry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        public int Users;
    }

    private readonly Dictionary<string, GateEntry> _gates = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeWriteGate(string gateId, TimeSpan? waitTimeout = null, INodeDiagnosticsSink? diagnostics = null)
    {
        GateId = gateId;
        WaitTimeout = waitTimeout ?? TimeSpan.Zero;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public string GateId { get; }

    /// <summary>有界等待上限；<see cref="TimeSpan.Zero"/> 即纯 fail-fast（默认）。</summary>
    public TimeSpan WaitTimeout { get; }

    public async Task<T> RunAsync<T>(string target, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        var entry = Retain(target);
        var acquired = WaitTimeout <= TimeSpan.Zero
            ? entry.Gate.Wait(0, cancellationToken)
            : await entry.Gate.WaitAsync(WaitTimeout, cancellationToken).ConfigureAwait(false);

        if (!acquired)
        {
            Release(target, entry);
            _diagnostics.Report(new NodeDiagnostic(
                "write.gate-conflict",
                NodeDiagnosticSeverity.Error,
                "同 target 平行写被拒（fail-fast，不排队）",
                $"{GateId}|{target}"));
            throw new NodeWriteConflictException(target, WaitTimeout);
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
            Release(target, entry);
        }
    }

    public Task RunAsync(string target, Func<Task> action, CancellationToken cancellationToken = default) =>
        RunAsync<object?>(target, async () =>
        {
            await action().ConfigureAwait(false);
            return null;
        }, cancellationToken);

    public bool IsBusy(string target)
    {
        lock (_sync)
        {
            return _gates.TryGetValue(target, out var entry) && entry.Gate.CurrentCount == 0;
        }
    }

    private GateEntry Retain(string target)
    {
        lock (_sync)
        {
            if (!_gates.TryGetValue(target, out var entry))
            {
                entry = new GateEntry();
                _gates[target] = entry;
            }

            entry.Users++;
            return entry;
        }
    }

    private void Release(string target, GateEntry entry)
    {
        lock (_sync)
        {
            entry.Users--;
            if (entry.Users <= 0)
            {
                _gates.Remove(target);
            }
        }
    }
}
