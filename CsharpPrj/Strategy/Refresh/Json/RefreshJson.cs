using System.Text.Json;
using System.Text.Json.Serialization;
using Strategy.Refresh.Core;

namespace Strategy.Refresh.Json
{
    /// <summary>
    /// 刷新 JSON 门面（L1）：把注册面 / 运行态投影为**稳定 JSON**（工具面 / 诊断消费）。
    /// <para>错误一律收敛为 <c>{"status":"error","message":...,"sources":[...]}</c> 信封，不抛出到调用方。</para>
    /// <para><b>【预留】</b>预定消费方：工具面 / 诊断查询（经 <c>Invoke-Frame</c> 类入口传入 <c>RefreshService</c>）。
    /// 当前尚无调用方；启用前不得视为「已接入」。</para>
    /// </summary>
    public static class RefreshJson
    {
        /// <summary>统一入参。</summary>
        /// <param name="Slot">按槽键过滤（null = 全部）。</param>
        /// <param name="IncludeStats">是否附带运行态统计。</param>
        public sealed record RefreshQuery(string? Slot = null, bool IncludeStats = false);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>查询刷新框架状态 → 稳定 JSON（slot 过滤 + 源清单 + 可选统计）。</summary>
        public static string Query(RefreshService service, RefreshQuery? query = null)
        {
            ArgumentNullException.ThrowIfNull(service);
            query ??= new RefreshQuery();
            try
            {
                var slots = service.Registry.RendererSlotKeys;
                var targets = query.Slot is null
                    ? slots
                    : slots.Where(s => string.Equals(s, query.Slot, StringComparison.Ordinal)).ToArray();

                var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["status"] = "ok",
                    ["slot"] = query.Slot,
                    ["slots"] = targets,
                    ["sources"] = SourceList(service),
                };
                if (query.IncludeStats)
                {
                    envelope["stats"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["running"] = service.IsRunning,
                        ["pushCount"] = service.PushCount,
                        ["unchangedDeliveries"] = service.UnchangedDeliveries,
                        ["suppressedHeartbeats"] = service.SuppressedHeartbeats,
                    };
                    envelope["slotStates"] = targets.Select(slot =>
                    {
                        var state = service.Store.GetState(slot);
                        return (object)new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["slot"] = slot,
                            ["outcome"] = state.LastOutcome.ToString(),
                            ["lastVersion"] = state.LastDeliveredVersion,
                            ["lastFingerprint"] = state.LastFingerprint,
                            ["consecutiveNoChange"] = state.ConsecutiveNoChange,
                            ["deliveryFailures"] = state.ConsecutiveDeliveryFailures,
                            ["nextDelayMs"] = (long)state.NextDelay.TotalMilliseconds,
                            ["progressToken"] = state.LastProgressToken,
                            ["noProgressSince"] = state.NoProgressSince,
                        };
                    }).ToArray();
                }
                return JsonSerializer.Serialize(envelope, Options);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["status"] = "error",
                    ["message"] = ex.Message,
                    ["sources"] = SourceList(service),
                }, Options);
            }
        }

        /// <summary>契约常量快照（宿主 / 页面两端同源的复算面）。</summary>
        public static string Contract()
            => JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = "ok",
                ["command"] = Model.RefreshContract.Command,
                ["tickMs"] = Model.RefreshContract.TickMs,
                ["throttleMs"] = Model.RefreshContract.ThrottleMs,
                ["fallbackPollMs"] = Model.RefreshContract.FallbackPollMs,
                ["maxHeartbeatMs"] = Model.RefreshContract.MaxHeartbeatMs,
                ["stallAfterNoChange"] = Model.RefreshContract.StallAfterNoChange,
                ["stallWindowMs"] = Model.RefreshContract.StallWindowMs,
            }, Options);

        private static object[] SourceList(RefreshService service)
            => service.Registry.SourceDescriptors.Select(static s => (object)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = s.SourceId,
                ["name"] = s.Name,
                ["priority"] = s.Priority.ToString(),
                ["tickMs"] = s.TickMs,
                ["subscribeKey"] = s.SubscribeKey,
            }).ToArray();
    }
}
