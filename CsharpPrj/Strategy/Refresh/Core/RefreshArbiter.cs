using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>一次仲裁结果：决策 + 交付事实 + 是否被在飞合并。</summary>
    public sealed record RefreshArbitrationResult(
        AcceptDecision Decision,
        DeliveryResult Delivery,
        bool Merged);

    /// <summary>
    /// 写仲裁引擎（L2）：每 slot 单调版本 + baseline 过期即丢 + 在飞 latest-wins 合并。
    /// <para>只有 <see cref="DeliveryResult.Delivered"/> 且 <see cref="DeliveryResult.ContentChanged"/> 才推进 baseline。</para>
    /// <para>在飞期间到达的更新只保留最新版本；旧版本完成时不推进 baseline（不覆盖更新的）。</para>
    /// </summary>
    public sealed class RefreshArbiter
    {
        private readonly IRefreshStore _store;
        private readonly IRefreshSink _sink;
        private readonly Action<string>? _report;
        private readonly ConcurrentDictionary<string, Flight> _flights = new(StringComparer.Ordinal);

        public RefreshArbiter(IRefreshStore store, IRefreshSink sink, Action<string>? report = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _report = report;
        }

        private sealed record Request(RefreshTicket Ticket, string Fingerprint, Func<object> Build);

        /// <summary>每 slot 的在飞状态：至多一个在飞交付；在飞期到达的更新请求合并入 <see cref="Pending"/>。</summary>
        private sealed class Flight
        {
            internal readonly object Gate = new();
            internal bool InFlight;
            internal long CurrentVersion; // 当前正在交付的版本（防旧版本回写）
            internal Request? Pending;     // 在飞期合并的"最新一次"（按版本取更高者）
            internal TaskCompletionSource<RefreshArbitrationResult> Done =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// 提交一次写作请求：先判定（Skip/Stale 不触写落地），Apply 则经单飞 + latest-wins 合并落地。
        /// </summary>
        public async Task<RefreshArbitrationResult> CommitAsync(
            RefreshTicket ticket,
            string fingerprint,
            Func<object> build,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(build);

            var decision = _store.Decide(ticket.SlotKey, ticket.Version, fingerprint);
            if (decision != AcceptDecision.Apply)
            {
                _report?.Invoke($"[Refresh] {ticket.SlotKey} v{ticket.Version} => {decision}（不触写落地） trace={ticket.TraceId ?? "-"}");
                return new RefreshArbitrationResult(decision, DeliveryResult.NotDelivered(), false);
            }

            var flight = _flights.GetOrAdd(ticket.SlotKey, _ => new Flight());
            var request = new Request(ticket, fingerprint, build);
            TaskCompletionSource<RefreshArbitrationResult> done;
            bool becomeDrainer;
            lock (flight.Gate)
            {
                if (flight.InFlight)
                {
                    // 在飞：latest-wins 合并 —— 仅接收比当前在飞版本更高者（防旧快照回写）
                    if (ticket.Version > flight.CurrentVersion)
                        flight.Pending = request;
                    done = flight.Done;
                    becomeDrainer = false;
                }
                else
                {
                    flight.InFlight = true;
                    flight.Done = new TaskCompletionSource<RefreshArbitrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    done = flight.Done;
                    becomeDrainer = true;
                }
            }

            if (becomeDrainer)
                _ = DrainAsync(flight, request, done, ct);

            return await done.Task.ConfigureAwait(false);
        }

        /// <summary>单飞 drain：串行执行该 slot 的写；执行期间到达的更新被合并，末态以最新一并落地。</summary>
        private async Task DrainAsync(
            Flight flight,
            Request first,
            TaskCompletionSource<RefreshArbitrationResult> done,
            CancellationToken ct)
        {
            var current = first;
            var merged = false;
            try
            {
                while (true)
                {
                    var payload = current.Build(); // 每个被接受的请求只渲染一次
                    lock (flight.Gate)
                        flight.CurrentVersion = current.Ticket.Version;

                    var delivery = new RefreshDelivery(
                        current.Ticket.SlotKey, payload, current.Ticket.Version, current.Fingerprint, current.Ticket.TraceId);

                    DeliveryResult result;
                    try
                    {
                        result = await _sink.DeliverAsync(delivery, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = DeliveryResult.NotDelivered(ex);
                    }

                    Request? next;
                    lock (flight.Gate)
                    {
                        next = flight.Pending;
                        flight.Pending = null;
                        if (next is null)
                            flight.InFlight = false; // 无待交付：释放槽位
                    }

                    if (next is null)
                    {
                        // 只有「送达且内容变化」才推进 baseline（未送达 / 未变化不推进）。
                        if (result.Delivered && result.ContentChanged)
                            _store.Commit(current.Ticket.SlotKey, current.Ticket.Version, current.Fingerprint);
                        _report?.Invoke(
                            $"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} drained " +
                            $"delivered={result.Delivered} changed={result.ContentChanged} trace={current.Ticket.TraceId ?? "-"}");
                        done.TrySetResult(new RefreshArbitrationResult(AcceptDecision.Apply, result, merged));
                        return;
                    }

                    // 有待交付的更新：旧版本不推进 baseline（旧的在飞完成后不得覆盖更新的）
                    merged = true;
                    _report?.Invoke(
                        $"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} 在飞合并（latest-wins）→ v{next.Ticket.Version}");
                    current = next;
                }
            }
            catch (Exception ex)
            {
                lock (flight.Gate)
                {
                    flight.InFlight = false;
                    flight.Pending = null;
                }
                _report?.Invoke($"[Refresh] {current.Ticket.SlotKey} drain 异常: {ex.GetType().Name}: {ex.Message}");
                done.TrySetResult(new RefreshArbitrationResult(
                    AcceptDecision.Apply,
                    DeliveryResult.NotDelivered(ex),
                    merged));
            }
        }
    }
}


