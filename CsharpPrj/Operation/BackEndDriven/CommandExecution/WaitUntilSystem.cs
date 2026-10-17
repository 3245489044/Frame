#nullable enable

using ECS.Components;
using ECS.Core;
using BackEndDriven.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// wait-until 命令（B6·有界轮询，替代固定 sleep）：按 interval 周期调用探测函数，
/// 直到其返回文本包含 expectedContains 或到达 timeout；命中后把最后一次文本回写 output 控件。
/// <para>
/// 语义（对齐设计单 §B6）：<c>wait-until &lt;probeFn&gt; expectedContains=&lt;text&gt; timeout=&lt;ms&gt; interval=&lt;ms&gt; [output=&lt;control&gt;]</c>；
/// 失败必须落在强判据（超时 + 最后一次文本可见），而非「没跑完」。固定 sleep 由
/// <c>Check-ScriptNoFixedSleep.ps1</c> 拦截。
/// </para>
/// <para>探测函数 = 只读生产者（如 conversation/history）；本命令只调用、不写业务状态。</para>
/// </summary>
public class WaitUntilSystem : CommandSystemBase, IAsyncCommandSystem
{
    /// <summary>默认总预算（真实 LLM/daemon 链路 ≥ 固定 sleep 90000 的历史实测上界）。</summary>
    public const int DefaultTimeoutMs = 300_000;

    /// <summary>默认轮询间隔（只读探测，避免高频打库）。</summary>
    public const int DefaultIntervalMs = 2_000;

    private readonly IEcsStateAccessor? _stateAccessor;

    public WaitUntilSystem(EntityManager entityManager)
        : base(entityManager, "wait-until", "waituntil")
    {
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    /// <summary>同步路径（legacy 调用方）委托异步实现；主脚本路径走非阻塞异步。</summary>
    public override void Execute(Entity entity, CommandMetaComponent meta)
        => ExecuteAsync(entity, meta).GetAwaiter().GetResult();

    public async Task ExecuteAsync(Entity entity, CommandMetaComponent meta)
    {
        var probe = meta.Target;
        if (string.IsNullOrWhiteSpace(probe))
        {
            SetFailure(entity, "wait-until: 缺少探测函数名");
            return;
        }

        if (!TryParse(meta, out var parameters, out var expectedContains, out var timeoutMs, out var intervalMs, out var outputTarget))
        {
            SetFailure(entity, "wait-until: 缺少 expectedContains（空期望恒真，拒绝静默通过）");
            return;
        }

        var deadline = timeoutMs > 0 ? Environment.TickCount64 + timeoutMs : long.MaxValue;
        string? lastText = null;
        var matched = false;

        while (true)
        {
            try
            {
                var invocation = await EventPlatformExecutor.InvokeAsync(probe, parameters, null).ConfigureAwait(false);
                if (!invocation.PlatformAvailable || invocation.Result is null)
                {
                    lastText = invocation.Error;
                }
                else if (!invocation.Result.IsSuccess)
                {
                    lastText = invocation.Result.ErrorMessage;
                }
                else
                {
                    lastText = invocation.Result.Output?.ToString() ?? string.Empty;
                    matched = lastText.IndexOf(expectedContains, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                lastText = ex.Message;
            }

            if (matched || Environment.TickCount64 >= deadline) break;
            await Task.Delay(intervalMs).ConfigureAwait(false);
        }

        if (matched)
        {
            if (_stateAccessor != null && !string.IsNullOrWhiteSpace(outputTarget))
                _stateAccessor.SetValue(outputTarget!, lastText);

            SetSuccess(entity, lastText);
            return;
        }

        var detail = string.IsNullOrWhiteSpace(lastText) ? "(空)" : LogTextTruncation.TruncateForLog(lastText);
        SetFailure(entity,
            $"wait-until '{probe}' 超时（{timeoutMs}ms）: 结果不含 '{expectedContains}' — 最后一次 '{detail}'");
    }

    /// <summary>解析命令面：expectedContains/contains（条件）、timeout/interval/output（命令级），其余为探测函数参数。</summary>
    private static bool TryParse(
        CommandMetaComponent meta,
        out Dictionary<string, object?> parameters,
        out string expectedContains,
        out int timeoutMs,
        out int intervalMs,
        out string? outputTarget)
    {
        parameters = new Dictionary<string, object?>();
        expectedContains = string.Empty;
        timeoutMs = DefaultTimeoutMs;
        intervalMs = DefaultIntervalMs;
        outputTarget = null;

        if (meta.Parameters != null)
        {
            foreach (var kvp in meta.Parameters)
                parameters[kvp.Key] = kvp.Value;
        }

        if (parameters.TryGetValue("expectedContains", out var ec) && ec != null)
        {
            expectedContains = ec.ToString() ?? string.Empty;
            parameters.Remove("expectedContains");
        }
        else if (parameters.TryGetValue("contains", out var c) && c != null)
        {
            expectedContains = c.ToString() ?? string.Empty;
            parameters.Remove("contains");
        }

        if (string.IsNullOrWhiteSpace(expectedContains)) return false;

        if (parameters.TryGetValue("timeout", out var t) && int.TryParse(t?.ToString(), out var tv))
        {
            timeoutMs = tv;
            parameters.Remove("timeout");
        }

        if (parameters.TryGetValue("interval", out var iv) && int.TryParse(iv?.ToString(), out var ivv) && ivv > 0)
        {
            intervalMs = ivv;
            parameters.Remove("interval");
        }

        if (parameters.TryGetValue("output", out var o) && o != null)
        {
            outputTarget = o.ToString();
            parameters.Remove("output");
        }

        return true;
    }
}
