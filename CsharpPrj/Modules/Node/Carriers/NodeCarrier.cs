using Modules.Node.Diagnostics;

namespace Modules.Node.Carriers;

public enum CarrierWriteStatus
{
    Written,

    /// <summary>同 (Key, Placement, Digest) 重放：无副作用（N4 幂等）。</summary>
    Unchanged,

    /// <summary>异 Digest 同位置：可视化为冲突，不静默覆盖（N5）。</summary>
    Conflict,

    /// <summary>载体拒写（Legacy / 语义不允许）。</summary>
    Rejected,
}

public sealed record CarrierWriteResult(CarrierWriteStatus Status, string? Reason = null)
{
    public static CarrierWriteResult Written() => new(CarrierWriteStatus.Written);

    public static CarrierWriteResult Unchanged(string? reason = null) => new(CarrierWriteStatus.Unchanged, reason);

    public static CarrierWriteResult Conflict(string reason) => new(CarrierWriteStatus.Conflict, reason);

    public static CarrierWriteResult Rejected(string reason) => new(CarrierWriteStatus.Rejected, reason);
}

public sealed record CarrierRecord(string Key, string Json, string? Digest, DateTimeOffset WrittenAt);

/// <summary>
/// 载体四原语（N4/N5/N10）：载体细节（库/表/控件）只在其实现内，不进框架。
/// 读写均以键 + JSON 字符串为界（D-1 案 A）。
/// </summary>
public interface INodeCarrier
{
    string CarrierId { get; }

    /// <summary>载体覆写语义（补偿可行性判据：只增不丢 ⇒ 历史写入不可撤销）。</summary>
    CarrierSemantics Semantics { get; }

    Task EnsureAsync(string key, CancellationToken cancellationToken = default);

    Task<CarrierWriteResult> UpsertAsync(string key, string json, CancellationToken cancellationToken = default);

    Task<CarrierRecord?> ReadAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> EnumerateKeysAsync(string? keyPrefix = null, CancellationToken cancellationToken = default);
}

/// <summary>载体注册表（装配期校验 "carrier 已注册" 的查询面）。</summary>
public interface ICarrierRegistry
{
    void Register(INodeCarrier carrier);

    bool TryGet(string carrierId, out INodeCarrier carrier);

    IReadOnlyCollection<INodeCarrier> All { get; }
}

public sealed class CarrierRegistry : ICarrierRegistry
{
    private readonly Dictionary<string, INodeCarrier> _carriers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<INodeCarrier> All => _carriers.Values.ToArray();

    public void Register(INodeCarrier carrier)
    {
        if (!_carriers.TryAdd(carrier.CarrierId, carrier))
        {
            throw new InvalidOperationException($"载体重复注册: {carrier.CarrierId}");
        }
    }

    public bool TryGet(string carrierId, out INodeCarrier carrier)
    {
        if (_carriers.TryGetValue(carrierId, out var found))
        {
            carrier = found;
            return true;
        }

        carrier = null!;
        return false;
    }
}

/// <summary>未装配载体：一切写读即 ∅ + 诊断，不阻断其它位置（装配期记为 Unbound）。</summary>
public sealed class NullNodeCarrier : INodeCarrier
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullNodeCarrier(string carrierId, INodeDiagnosticsSink? diagnostics = null)
    {
        CarrierId = carrierId;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public string CarrierId { get; }

    public CarrierSemantics Semantics => CarrierSemantics.AppendOnly;

    public Task EnsureAsync(string key, CancellationToken cancellationToken = default)
    {
        Report("ensure", key);
        return Task.CompletedTask;
    }

    public Task<CarrierWriteResult> UpsertAsync(string key, string json, CancellationToken cancellationToken = default)
    {
        Report("upsert", key);
        return Task.FromResult(CarrierWriteResult.Rejected("carrier-unbound"));
    }

    public Task<CarrierRecord?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        Report("read", key);
        return Task.FromResult<CarrierRecord?>(null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Report("delete", key);
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<string>> EnumerateKeysAsync(string? keyPrefix = null, CancellationToken cancellationToken = default)
    {
        Report("enumerate", keyPrefix ?? string.Empty);
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private void Report(string operation, string subject) =>
        _diagnostics.Report(new NodeDiagnostic(
            "carrier.unbound",
            NodeDiagnosticSeverity.Warning,
            $"载体未装配，操作 '{operation}' 已跳过（不兜底）",
            $"{CarrierId}|{subject}"));
}
