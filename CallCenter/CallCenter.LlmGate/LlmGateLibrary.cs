using CallCenter.LlmGate.Formatting;
using CallCenter.LlmGate.Model;
using CallCenter.LlmGate.Ports;
using CommandFramework.Abstractions;
using Modules.Registry;
using TConfig;

namespace CallCenter.LlmGate;

/// <summary>
/// LlmGate 窗口函数库（JSON UI 唯一绑定面）。
/// 经端口取服务：同进程静态槽优先，否则回退 <c>http://127.0.0.1:8790</c> 的 /api/gate/*；
/// 未装配/连接失败一律回写友好文本，不向 UI 线程抛异常。
/// </summary>
[FunctionLibrary]
public static class LlmGateLibrary
{
    public static void RegisterAll(ITrConfigRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("LlmGate_Status", (Func<string>)(() => WithControl(c => c.Status())),
            "LlmGate 状态（mode/listen/upstream/halted/uptime/配置路径）");

        registry.Register("LlmGate_Stats", (Func<string>)(() => FormatStats()),
            "LlmGate 当日统计（调用/成本/未知/拒绝/待审批/丢弃）");

        registry.Register("LlmGate_Calls", (Func<string?, ListData>)(filter => Calls(filter)),
            "LlmGate 调用流水（单列文本；filter 匹配 id/会话/任务/模型/指纹）");

        registry.Register("LlmGate_Pending", (Func<ListData>)(() => Pending()),
            "LlmGate 待审批列表（单列文本）");

        registry.Register("LlmGate_Detail", (Func<string, string>)(selected => Detail(selected)),
            "LlmGate 调用详情（请求/响应/用量/成本/裁决/指纹/时间线）");

        registry.Register("LlmGate_Approve", (Func<string, ListData>)(selected =>
        {
            var callId = LlmCallFormatter.ParseCallId(selected);
            if (callId is not null)
            {
                WithControl(c =>
                {
                    c.TryApprove(callId, out _);
                    return string.Empty;
                });
            }

            return Pending();
        }), "放行选中待审批调用（DecidedBy=manual）");

        registry.Register("LlmGate_Deny", (Func<string, string?, ListData>)((selected, reason) =>
        {
            var callId = LlmCallFormatter.ParseCallId(selected);
            if (callId is not null)
            {
                WithControl(c =>
                {
                    c.TryDeny(callId, reason, out _);
                    return string.Empty;
                });
            }

            return Pending();
        }), "拒绝选中待审批调用（403 + LG_DENY_MANUAL）");

        registry.Register("LlmGate_Halt", (Func<string?, string>)(reason => WithControl(c => c.Halt(reason))),
            "kill switch：立即拦截全部请求（503）");

        registry.Register("LlmGate_Resume", (Func<string>)(() => WithControl(c => c.Resume())),
            "解除 Halt（不清账）");

        registry.Register("LlmGate_SetMode", (Func<string, string>)(mode => WithControl(c => c.SetMode(mode))),
            "切换模式（enforce/manual/off；非法值不改状态）");
    }

    // ---- 实现 ----

    private static string FormatStats()
    {
        try
        {
            var ports = LlmGatePorts.ResolveOrRemote();
            return ports.Query is null
                ? ports.Message ?? NotBound()
                : LlmCallFormatter.FormatStats(ports.Query.SnapshotStats());
        }
        catch (Exception ex)
        {
            return NotBound(ex);
        }
    }

    private static ListData Calls(string? filter)
    {
        try
        {
            var ports = LlmGatePorts.ResolveOrRemote();
            if (ports.Query is null)
            {
                return ListData.From(new[] { ports.Message ?? NotBound() });
            }

            var spans = ports.Query.SnapshotCalls(filter, 500);
            return spans.Count == 0
                ? ListData.From(new[] { "（无调用记录）" })
                : ListData.From(spans.Select(LlmCallFormatter.FormatCallLine));
        }
        catch (Exception ex)
        {
            return ListData.From(new[] { NotBound(ex) });
        }
    }

    private static ListData Pending()
    {
        try
        {
            var ports = LlmGatePorts.ResolveOrRemote();
            if (ports.Query is null)
            {
                return ListData.From(new[] { ports.Message ?? NotBound() });
            }

            var pending = ports.Query.SnapshotPending();
            return pending.Count == 0
                ? ListData.From(new[] { "（无待审批）" })
                : ListData.From(pending.Select(LlmCallFormatter.FormatPendingLine));
        }
        catch (Exception ex)
        {
            return ListData.From(new[] { NotBound(ex) });
        }
    }

    private static string Detail(string? selected)
    {
        var callId = LlmCallFormatter.ParseCallId(selected);
        if (callId is null)
        {
            return "未选择调用";
        }

        try
        {
            var ports = LlmGatePorts.ResolveOrRemote();
            if (ports.Query is null)
            {
                return ports.Message ?? NotBound();
            }

            var span = ports.Query.Find(callId);
            return span is null ? $"未找到调用：{callId}" : LlmCallFormatter.FormatDetail(span);
        }
        catch (Exception ex)
        {
            return NotBound(ex);
        }
    }

    private static string WithControl(Func<ILlmGateControl, string> action)
    {
        try
        {
            var ports = LlmGatePorts.ResolveOrRemote();
            return ports.Control is null
                ? ports.Message ?? NotBound()
                : action(ports.Control);
        }
        catch (Exception ex)
        {
            return NotBound(ex);
        }
    }

    private static string NotBound(Exception? ex = null)
        => ex is null
            ? $"LlmGate 未装配（无法连接 {LlmGatePorts.RemoteUrl}）"
            : $"LlmGate 未装配（{ex.Message}）";
}
