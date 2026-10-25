using Modules.Wiring.Compile;
using Modules.Wiring.Model;

namespace Modules.Wiring.Runtime;

/// <summary>运行期唯一投递入口（禁旁路直呼：不经 Junction 的连接视为未接线）。</summary>
public interface IJunction
{
    ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        CancellationToken cancellationToken = default);

    ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>源端口发布：按 <c>From</c> 索引扇出全部出边（顺序 = plan 顺序，可复算）。</summary>
    ValueTask<IReadOnlyList<WireResult>> PublishAsync(
        string sourceRef,
        ValueEnvelope envelope,
        string scope = WireScopeRouter.Global,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Junction：take → adapt → judge → merge → post → ledger（顺序固定，唯一投递点）。
/// <para>handler 表缺项 ⇒ <c>WIRE_HANDLER_UNBOUND</c> 显式失败；<see cref="WireDeliveryMode.Shadow"/> 只记账不执行。</para>
/// </summary>
public sealed class Junction : IJunction
{
    private readonly WiringPlan _plan;
    private readonly IWireLedger _ledger;
    private readonly string _generation;
    private readonly IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>> _adapters;
    private readonly IWireHandlerTable _handlers;
    private readonly Action<string, ValueEnvelope>? _postbox;
    private readonly WireDeliveryMode _mode;
    private readonly IWirePortStore _portStore;
    private readonly bool _strictGeneration;
    private readonly Func<string>? _liveGeneration;
    private readonly Dictionary<string, int> _deliveries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Junction(
        WiringPlan plan,
        IWireLedger? ledger = null,
        string generation = WiringFreezer.LiveGeneration,
        IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>>? adapters = null,
        Action<string, ValueEnvelope>? postbox = null,
        IWireHandlerTable? handlers = null,
        WireDeliveryMode mode = WireDeliveryMode.Primary,
        IWirePortStore? portStore = null,
        bool strictGeneration = false,
        Func<string>? liveGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plan = plan;
        _ledger = ledger ?? NullWireLedger.Instance;
        _generation = string.IsNullOrWhiteSpace(generation) ? WiringFreezer.LiveGeneration : generation;
        _adapters = adapters ?? new Dictionary<string, Func<ValueEnvelope, ValueEnvelope>>(StringComparer.Ordinal);
        _postbox = postbox;
        _handlers = handlers ?? WireHandlerTable.Empty;
        _mode = mode;
        _portStore = portStore ?? new InMemoryWirePortStore();
        _strictGeneration = strictGeneration;
        _liveGeneration = liveGeneration;
    }

    public IWireLedger Ledger => _ledger;

    public WireDeliveryMode Mode => _mode;

    public IWirePortStore PortStore => _portStore;

    public ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        CancellationToken cancellationToken = default)
        => DeliverAsync(wireId, envelope, envelope.Scope ?? WireScopeRouter.Global, cancellationToken);

    public async ValueTask<WireResult> DeliverAsync(
        string wireId,
        ValueEnvelope envelope,
        string scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!WireScopeRouter.TryResolve(scope, out var instance))
        {
            var invalid = new WireResult(WireStatus.Skipped, wireId, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireScopeEscape + ":invalid-scope:" + scope);
            Record(invalid, envelope.Form, scope, null);
            return invalid;
        }

        var effectiveScope = instance.Scope;

        if (IsStaleGeneration())
        {
            var stale = new WireResult(WireStatus.Skipped, wireId, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireStaleGeneration + ":" + _generation);
            Record(stale, envelope.Form, effectiveScope, null);
            return stale;
        }

        var wire = _plan.Find(wireId);
        if (wire is null)
        {
            var unknown = new WireResult(WireStatus.Skipped, wireId, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireUnknownWire);
            Record(unknown, envelope.Form, effectiveScope, null);
            return unknown;
        }

        var current = envelope;
        var adapted = false;

        // 判据载体 = 原始事件信封（适配只改形态，不改事件语义）。
        if (wire.Link.Judge is not null && !wire.Link.Judge(envelope))
        {
            var judgedOut = new WireResult(WireStatus.Skipped, wireId, envelope.Length, "judge-false");
            Record(judgedOut, envelope.Form, effectiveScope, wire.Link.To.Ref);
            return judgedOut;
        }

        if (wire.Link.Adapter is not null)
        {
            if (_adapters.TryGetValue(wire.Link.Adapter.Id, out var adapt))
            {
                current = adapt(envelope);
                adapted = true;
            }
            else if (!string.Equals(envelope.Form, wire.Link.To.Form, StringComparison.Ordinal))
            {
                var unbound = new WireResult(WireStatus.Skipped, wireId, envelope.Length,
                    Wiring.Diagnostics.WiringReasonCodes.WireAdapterUnbound + ":" + wire.Link.Adapter.Id);
                Record(unbound, envelope.Form, effectiveScope, wire.Link.To.Ref);
                return unbound;
            }
        }

        if (!string.Equals(current.Form, wire.Link.To.Form, StringComparison.Ordinal))
        {
            var drift = new WireResult(WireStatus.Skipped, wireId, current.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireValueFormDrift + ":" + current.Form + "->" + wire.Link.To.Form);
            Record(drift, current.Form, effectiveScope, wire.Link.To.Ref);
            return drift;
        }

        // 端口即值槽（I10）：required 端口收到空值即作废旧值（防空文本复用上一稿）；
        // 非 required 端口无载荷时可取仓内既有值。
        if (!current.HasPayload)
        {
            if (wire.Link.To.Required)
            {
                if (_mode != WireDeliveryMode.Shadow)
                {
                    _portStore.Set(effectiveScope, wire.Link.To.Ref,
                        new ValueEnvelope(wire.Link.To.Form, null, current.TraceId, current.OpId, current.ProducerWireId, effectiveScope));
                }
            }
            else if (_portStore.TryGet(effectiveScope, wire.Link.To.Ref, out var stored)
                     && stored.HasPayload
                     && string.Equals(stored.Form, wire.Link.To.Form, StringComparison.Ordinal))
            {
                current = stored;
            }
        }

        var result = await wire.Route(current).ConfigureAwait(false);

        if (result.Status == WireStatus.MissingRequired)
        {
            var missing = ApplyOnMissing(wire, result, ref current);
            if (missing is not null)
            {
                Record(missing.Value, current.Form, effectiveScope, wire.Link.To.Ref);
                return missing.Value;
            }
        }
        else if (result.Status == WireStatus.Skipped)
        {
            Record(result, current.Form, effectiveScope, wire.Link.To.Ref);
            return result;
        }

        if (_mode == WireDeliveryMode.Shadow)
        {
            Record(result with { Reason = result.Reason ?? "shadow" }, current.Form, effectiveScope, wire.Link.To.Ref);
            return result;
        }

        var merge = CheckMerge(wire);
        if (merge is not null)
        {
            Record(merge.Value, current.Form, effectiveScope, wire.Link.To.Ref);
            return merge.Value;
        }

        // 写入即供给（Primary 才写入；Shadow 只记账不产生副作用）。
        if (current.HasPayload)
            _portStore.Set(effectiveScope, wire.Link.To.Ref, current with { Scope = effectiveScope });

        var unsatisfied = FindUnsatisfiedRequiredPort(wire, effectiveScope);
        if (unsatisfied is not null)
        {
            var missingPort = new WireResult(WireStatus.MissingRequired, wireId, current.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireMissingRequired + ":" + unsatisfied);
            Record(missingPort, current.Form, effectiveScope, wire.Link.To.Ref);
            return missingPort;
        }

        if (!_handlers.TryGet(wire.Link.To.Owner, out var handler))
        {
            var unboundHandler = new WireResult(WireStatus.MissingRequired, wireId, current.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireHandlerUnbound + ":" + wire.Link.To.Owner);
            Record(unboundHandler, current.Form, effectiveScope, wire.Link.To.Ref);
            return unboundHandler;
        }

        try
        {
            var context = new WireDeliveryContext(current, effectiveScope, wire.Link.To.Owner, _portStore);
            await handler(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failed = new WireResult(WireStatus.Skipped, wireId, current.Length,
                "handler-error:" + exception.GetType().Name);
            Record(failed, current.Form, effectiveScope, wire.Link.To.Ref);
            return failed;
        }

        if (adapted && result.Status == WireStatus.Delivered)
            result = result with { Status = WireStatus.Adapted };

        _postbox?.Invoke(wireId, current);
        Record(result, current.Form, effectiveScope, wire.Link.To.Ref);
        return result;
    }

    /// <summary>源端口扇出：一发多边（同一事件的多条出边一次投递，顺序 = plan 顺序）。</summary>
    public async ValueTask<IReadOnlyList<WireResult>> PublishAsync(
        string sourceRef,
        ValueEnvelope envelope,
        string scope = WireScopeRouter.Global,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!WireScopeRouter.TryResolve(scope, out var instance))
        {
            var invalid = new WireResult(WireStatus.Skipped, "publish:" + sourceRef, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireScopeEscape + ":invalid-scope:" + scope);
            Record(invalid, envelope.Form, scope, null);
            return new[] { invalid };
        }

        if (IsStaleGeneration())
        {
            var stale = new WireResult(WireStatus.Skipped, "publish:" + sourceRef, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireStaleGeneration + ":" + _generation);
            Record(stale, envelope.Form, instance.Scope, null);
            return new[] { stale };
        }

        var edges = _plan.From(sourceRef);
        if (edges.Count == 0)
        {
            var orphan = new WireResult(WireStatus.Skipped, "publish:" + sourceRef, envelope.Length,
                Wiring.Diagnostics.WiringReasonCodes.WireOrphanSource + ":" + sourceRef);
            Record(orphan, envelope.Form, instance.Scope, null);
            return new[] { orphan };
        }

        var results = new List<WireResult>(edges.Count);
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await DeliverAsync(edge.Id, envelope, instance.Scope, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private bool IsStaleGeneration()
        => _strictGeneration
           && _liveGeneration is not null
           && !string.Equals(_generation, _liveGeneration(), StringComparison.Ordinal);

    /// <summary>盒级必填闸门：任一 required inport 无供值 ⇒ 不调用 handler（空值前移到接线缺供）。</summary>
    private string? FindUnsatisfiedRequiredPort(CompiledWire wire, string scope)
    {
        foreach (var portRef in _plan.RequiredFor(wire.Link.To.Owner))
        {
            if (!_portStore.Has(scope, portRef)) return portRef;
        }

        return null;
    }


    private WireResult? ApplyOnMissing(CompiledWire wire, WireResult missing, ref ValueEnvelope current)
    {
        switch (wire.Link.Spec.OnMissing)
        {
            case OnMissing.Skip:
                return missing with { Status = WireStatus.Skipped, Reason = "missing-required:skip" };

            case OnMissing.Report:
                return missing with { Reason = "missing-required:reported" };

            case OnMissing.Default:
                if (string.IsNullOrEmpty(wire.Link.Spec.Default))
                    return missing with { Reason = Wiring.Diagnostics.WiringReasonCodes.WireDefaultMissing + ":" + wire.Id };

                current = current with { Payload = wire.Link.Spec.Default };
                return null;

            default:
                return missing;
        }
    }

    private WireResult? CheckMerge(CompiledWire wire)
    {
        if (wire.Link.Spec.Merge == MergeMode.None) return null;

        if (wire.Link.Spec.Merge == MergeMode.Quorum)
        {
            return new WireResult(WireStatus.Skipped, wire.Id, 0,
                Wiring.Diagnostics.WiringReasonCodes.WireMergeUnsupported + ":quorum");
        }

        var target = wire.Link.To.Ref;
        lock (_gate)
        {
            _deliveries.TryGetValue(target, out var count);
            if (wire.Link.Spec.Merge == MergeMode.First && count > 0)
                return new WireResult(WireStatus.Skipped, wire.Id, 0, "merge-first:already-delivered");

            _deliveries[target] = count + 1;
        }

        return null;
    }

    private void Record(WireResult result, string form, string scope, string? port)
        => _ledger.Record(new WireLedgerRecord(
            result.WireId,
            (result.WireId + ":" + _generation).ToLowerInvariant(),
            form,
            result.Length,
            result.Status,
            result.Reason,
            _generation,
            scope,
            port));
}
