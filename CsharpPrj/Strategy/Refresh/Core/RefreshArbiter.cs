using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 写仲裁引擎（L2，根因治理核心）：每 slot 单调版本 + baseline 过期即丢 + 在飞 latest-wins 合并。
    /// <para>语义（不可协商）：</para>
    /// <list type="bullet">
    /// <item>先 <see cref="IRefreshStore.Decide"/>；<see cref="AcceptDecision.Skip"/>/<see cref="AcceptDecision.Stale"/> 不触 DOM，直接返回。</item>
    /// <item><see cref="AcceptDecision.Apply"/>：每 slot 单飞 + 在飞期到达的更新请求合并为"最新一次"；
    /// 旧的在飞完成后不得覆盖更新的（有更新待交付时，旧版本不推进 baseline）。</item>
    /// <item>落地成功（sink 返回 true）才 <see cref="IRefreshStore.Commit"/> 推进 baseline；返回 false 则不推进（可重试）。</item>
    /// </list>
    /// <para>单飞 drain 语义参考下沉执行器的 WriteSlot（仅参考语义，不引用其类型）。</para>
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
            internal TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// 提交一次写作请求：先判定（Skip/Stale 不触 DOM），Apply 则经单飞+latest-wins 合并落地。
        /// </summary>
        /// <returns>本次请求的决策（Skip/Stale/Apply）。</returns>
        public Task<AcceptDecision> CommitAsync(
            RefreshTicket ticket,
            string fingerprint,
            Func<object> build,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(build);

            var decision = _store.Decide(ticket.SlotKey, ticket.Version, fingerprint);
            if (decision != AcceptDecision.Apply)
            {
                _report?.Invoke($"[Refresh] {ticket.SlotKey} v{ticket.Version} => {decision}（不触 DOM） trace={ticket.TraceId ?? "-"}");
                return Task.FromResult(decision);
            }

            var flight = _flights.GetOrAdd(ticket.SlotKey, _ => new Flight());
            var request = new Request(ticket, fingerprint, build);
            TaskCompletionSource<bool> done;
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
                    flight.Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    done = flight.Done;
                    becomeDrainer = true;
                }
            }

            if (becomeDrainer)
                _ = DrainAsync(flight, request, done, ct);

            return AwaitDecisionAsync(done);
        }

        private static async Task<AcceptDecision> AwaitDecisionAsync(TaskCompletionSource<bool> done)
        {
            await done.Task.ConfigureAwait(false);
            return AcceptDecision.Apply;
        }

        /// <summary>单飞 drain：串行执行该 slot 的写；执行期间到达的更新被合并，末态以最新一并落地。</summary>
        private async Task DrainAsync(Flight flight, Request first, TaskCompletionSource<bool> done, CancellationToken ct)
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
                    var ok = await _sink.DeliverAsync(delivery, ct).ConfigureAwait(false);

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
                        // 落地成功才推进 baseline（未送达则不推进，供重试）；无更新时才是末次。
                        if (ok)
                            _store.Commit(current.Ticket.SlotKey, current.Ticket.Version, current.Fingerprint);
                        _report?.Invoke($"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} drained ok={ok} trace={current.Ticket.TraceId ?? "-"}");
                        break;
                    }

                    // 有待交付的更新：旧版本不推进 baseline（旧的在飞完成后不得覆盖更新的）
                    _report?.Invoke($"[Refresh] {current.Ticket.SlotKey} v{current.Ticket.Version} 在飞合并（latest-wins）→ v{next.Ticket.Version}");
                    current = next;
                }
                done.TrySetResult(true);
            }
            catch (Exception ex)
            {
                lock (flight.Gate)
                {
                    flight.InFlight = false;
                    flight.Pending = null;
                }
                _report?.Invoke($"[Refresh] {current.Ticket.SlotKey} drain 异常: {ex.GetType().Name}: {ex.Message}");
                done.TrySetException(ex);
            }
        }
    }
}

