using Modules.Wiring.Compile;

namespace Modules.Wiring.Runtime;

/// <summary>运行期唯一投递入口（禁旁路直呼：不经 Junction 的连接视为未接线）。</summary>
public interface IJunction
{
    ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Junction：take → adapt → post → ledger（顺序固定，唯一投递点）。
/// <para>适配委托与投递回调由上层在 Attach 阶段注入；未注入时显式落账（不静默跳过）。</para>
/// </summary>
public sealed class Junction : IJunction
{
    private readonly WiringPlan _plan;
    private readonly IWireLedger _ledger;
    private readonly string _generation;
    private readonly IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>> _adapters;
    private readonly Action<string, ValueEnvelope>? _postbox;

    public Junction(
        WiringPlan plan,
        IWireLedger? ledger = null,
        string generation = WiringFreezer.LiveGeneration,
        IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>>? adapters = null,
        Action<string, ValueEnvelope>? postbox = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plan = plan;
        _ledger = ledger ?? NullWireLedger.Instance;
        _generation = string.IsNullOrWhiteSpace(generation) ? WiringFreezer.LiveGeneration : generation;
        _adapters = adapters ?? new Dictionary<string, Func<ValueEnvelope, ValueEnvelope>>(StringComparer.Ordinal);
        _postbox = postbox;
    }

    public IWireLedger Ledger => _ledger;

    public async ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wire = _plan.Find(wireId);
        if (wire is null)
        {
            var unknown = new WireResult(WireStatus.Skipped, wireId, envelope.Length, Wiring.Diagnostics.WiringReasonCodes.WireUnknownWire);
            Record(unknown, envelope.Form);
            return unknown;
        }

        var current = envelope;
        var adapted = false;

        if (wire.Link.Adapter is not null)
        {
            if (_adapters.TryGetValue(wire.Link.Adapter.Id, out var adapt))
            {
                current = adapt(envelope);
                adapted = true;
            }
            else if (!string.Equals(envelope.Form, wire.Link.To.Form, StringComparison.Ordinal))
            {
                var unbound = new WireResult(
                    WireStatus.Skipped, wireId, envelope.Length,
                    Wiring.Diagnostics.WiringReasonCodes.WireAdapterUnbound + ":" + wire.Link.Adapter.Id);
                Record(unbound, envelope.Form);
                return unbound;
            }
        }

        if (!string.Equals(current.Form, wire.Link.To.Form, StringComparison.Ordinal))
        {
            var drift = new WireResult(
                WireStatus.Skipped, wireId, current.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireValueFormDrift + ":" + current.Form + "->" + wire.Link.To.Form);
            Record(drift, current.Form);
            return drift;
        }

        var result = await wire.Route(current).ConfigureAwait(false);
        if (adapted && result.Status == WireStatus.Delivered)
        {
            result = result with { Status = WireStatus.Adapted };
        }

        if (result.Status is WireStatus.Delivered or WireStatus.Adapted)
        {
            _postbox?.Invoke(wireId, current);
        }

        Record(result, current.Form);
        return result;
    }

    private void Record(WireResult result, string form)
        => _ledger.Record(new WireLedgerRecord(
            result.WireId,
            (result.WireId + ":" + _generation).ToLowerInvariant(),
            form,
            result.Length,
            result.Status,
            result.Reason,
            _generation));
}
