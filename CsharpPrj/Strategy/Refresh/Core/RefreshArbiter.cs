using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>一次仲裁结果：决策 + 交付事实 + 是否被在飞合并（被更高版本取代 / 该类请求并入本拍）。</summary>
    public sealed record RefreshArbitrationResult(
        AcceptDecision Decision,
        DeliveryResult Delivery,
        bool Merged);

    /// <summary>
    /// 写仲裁引擎（L2）：每 slot 单调版本 + baseline 过期即丢 + 在飞 latest-wins 合并。
    /// <para>只有 <see cref="DeliveryResult.Delivered"/> 且 <see cref="DeliveryResult.ContentChanged"/> 才推进 baseline。</para>
    /// <para>在飞期间到达的更新只保留最新版本；旧版本完成时不推进 baseline（不覆盖更新的）。</para>
    /// <para><b>结果归属</b>：每个调用方各自持有 <see cref="Request"/> 的结果槽——被本次在飞合并 / 被更高版本取代 /
    /// 被拒（版本不高于在飞版本）的调用方，都会得到「自己这次请求」的结论，绝不复用他人的交付事实。</para>
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

        /// <summary>一次写作请求：一人一份结果槽，避免并发调用方共用同一个 TCS。</summary>
        private sealed class Request
        {
            internal Request(RefreshTicket ticket, string fingerprint, Func<object> build)
            {
                Ticket = ticket;
                Fingerprint = fingerprint;
                Build = build;
            }

            internal readonly RefreshTicket Ticket;
            internal readonly string Fingerprint;
            internal readonly Func<object> Build;
            internal readonly TaskCompletionSource<RefreshArbitrationResult> Done =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>每 slot 的在飞状态：至多一个在飞交付；在飞期到达的更新请求合并入 <see cref="Pending"/>。</summary>
        private sealed class Flight
        {
            internal readonly object Gate = new();
            internal bool InFlight;
            internal long CurrentVersion; // 当前正在交付的版本（防旧版本回写）
            internal Request? Pending;     // 在飞期合并的"最新一次"（按版本取更高者）
        }

        /// <summary>
        /// 提交一次写作请求：先判定（Skip/Stale 不触写落地），Apply 则经单飞 + latest-wins 合并落地。
        /// <para>返回值为**本调用方**这次请求的结论：并入他人的合并拍 ⇒ Merged=true 且按自己的版本回绝（未交付）；
        /// 被更高版本取代的待交付请求 ⇒ 同样以 Merged=true 立即回绝，不阻塞调用方。</para>
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
            Request? superseded = null;
            long? refusedByVersion = null;
            bool becomeDrainer;
            lock (flight.Gate)
            {
                if (flight.InFlight)
                {
                    if (ticket.Version > flight.CurrentVersion)
                    {
                        // 在飞：latest-wins 合并 —— 仅接收比当前在飞版本更高者（防旧快照回写）。
                        // 被本轮取代的旧待交付请求，立即用它自己的版本回绝（不能等别人的交付结果）。
                        superseded = flight.Pending;
                        flight.Pending = request;
                        becomeDrainer = false;
                    }
                    else
                    {
                        // 版本不高于正在飞行的版本 ⇒ 本请求不参与落地，也不得 await 别人的结果槽。
                        refusedByVersion = flight.CurrentVersion;
                        becomeDrainer = false;
                    }
                }
                else
                {
                    flight.InFlight = true;
                    flight.CurrentVersion = ticket.Version;
                    becomeDrainer = true;
                }
            }

            if (refusedByVersion is long inFlightVersion)
            {
                _report?.Invoke(
                    $"[Refresh] {ticket.SlotKey} v{ticket.Version} 低于在飞 v{inFlightVersion} ⇒ 按 Stale 回绝 trace={ticket.TraceId ?? "-"}");
                return new RefreshArbitrationResult(AcceptDecision.Stale, DeliveryResult.NotDelivered(), true);
            }

            if (superseded is not null)
                CompleteSuperseded(superseded, ticket.Version);

            if (becomeDrainer)
                _ = DrainAsync(flight, request, ct);

            return await request.Done.Task.ConfigureAwait(false);
        }

        /// <summary>被更高版本取代的待交付请求：以自身版本即时回绝（未交付），不等待无关交付。</summary>
        private void CompleteSuperseded(Request superseded, long byVersion)
        {
            _report?.Invoke(
                $"[Refresh] {superseded.Ticket.SlotKey} v{superseded.Ticket.Version} 被 v{byVersion} 取代（未交付） trace={superseded.Ticket.TraceId ?? "-"}");
            superseded.Done.TrySetResult(
                new RefreshArbitrationResult(AcceptDecision.Apply, DeliveryResult.NotDelivered(), true));
        }

        /// <summary>单飞 drain：串行执行该 slot 的写；执行期间到达的更新被合并，末态以最新一并落地。</summary>
        private async Task DrainAsync(
            Flight flight,
            Request first,
            CancellationToken ct)
        {
            var current = first;
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

                    // 只有「送达且内容变化」才推进 baseline（未送达 / 未变化不推进）。
                    if (result.Delivered && result.ContentChanged)
                        _store.Commit(current.Ticket.SlotKey, current.Ticket.Version, current.Fingerprint);

                    _report?.Invoke(
                        $"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} drained " +
                        $"delivered={result.Delivered} changed={result.ContentChanged} trace={current.Ticket.TraceId ?? "-"}");
                    current.Done.TrySetResult(new RefreshArbitrationResult(AcceptDecision.Apply, result, false));

                    if (next is null) return;

                    // 有待交付的更新：旧版本不推进 baseline（旧的在飞完成后不得覆盖更新的）
                    _report?.Invoke(
                        $"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} 在飞合并（latest-wins）→ v{next.Ticket.Version}");
                    current = next;
                }
            }
            catch (Exception ex)
            {
                Request? pending;
                lock (flight.Gate)
                {
                    flight.InFlight = false;
                    pending = flight.Pending;
                    flight.Pending = null;
                }
                _report?.Invoke($"[Refresh] {current.Ticket.SlotKey} drain 异常: {ex.GetType().Name}: {ex.Message}");
                // 必须给本请求与待交付请求都落结果，否则调用方永久等待。
                current.Done.TrySetResult(new RefreshArbitrationResult(
                    AcceptDecision.Apply, DeliveryResult.NotDelivered(ex), false));
                pending?.Done.TrySetResult(new RefreshArbitrationResult(
                    AcceptDecision.Apply, DeliveryResult.NotDelivered(ex), true));
            }
        }
    }
}
