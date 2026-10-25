#nullable enable

using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace UiBackend.Html;

/// <summary>
/// 真实鼠标点击执行器（Win32 SendInput，isTrusted=true）——顺序：前台确权 → 元素级探测（rect + elementFromPoint 归属 +
/// scrollIntoView 复测）→ 坐标换算（dpr 显式校验 + DPI 交叉核对取证）→ 回执令牌安装 → 落点窗口归属 →
/// move/down/up → 单次回执回读 → 判定（仅 NOT_EFFECTIVE 允许一次重发）。
/// 送达判据只认「页面产生的 DOM click 回执（带 opId 令牌）」；探测只用于根因分型与坐标自证。
/// </summary>
public sealed class RealInputClicker
{
    /// <summary>真实点击「前台确权」等待上限（毫秒）。Windows 前台锁下激活可能需多次重试。</summary>
    private const int ForegroundTimeoutMs = 1500;

    /// <summary>回执等待上限（毫秒；C# 有界轮询——WebView2 不 await 页面 Promise，回读脚本必须是同步表达式）。</summary>
    private const int AckTimeoutMs = 900;

    /// <summary>回执轮询前的固定让路（毫秒）：给 Chromium 派发 click 与事件监听器执行留出时间。</summary>
    private const int AckInitialDelayMs = 60;

    /// <summary>回执轮询间隔（毫秒；≤900ms 窗口内最多约 8 次短往返，命中即返回）。</summary>
    private const int AckPollIntervalMs = 120;

    /// <summary>迟到回执复核窗口（毫秒）：先认回执再决定重发，避免双触发。</summary>
    private const int LateAckTimeoutMs = 200;

    /// <summary>最大尝试次数：首击 + 一次重发（仅 NOT_EFFECTIVE）。</summary>
    private const int MaxAttempts = 2;

    /// <summary>悬停收敛：先把指针移到目标（触发 hover 样式/位移），等待后再复测落点。</summary>
    private const int HoverSettleMs = 120;

    /// <summary>悬停复测的最大修正次数（位移收敛；超过即按最后一次落点发送，由回执裁决）。</summary>
    private const int MaxHoverCorrections = 2;

    private readonly IWebViewHost _host;
    private readonly HtmlScriptExecutor _executor;
    private readonly DiagnosticPort _diagnostics = new();

    public RealInputClicker(IWebViewHost host, HtmlScriptExecutor executor, Action<string, string>? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _diagnostics.Sink = diagnostics;
    }

    /// <summary>对具名元素执行真实点击（须在 UI 线程调用）。
    /// 返回 OK|… / NOT_FOUND|… / NOT_VISIBLE / OFF_VIEWPORT|… / OCCLUDED|… / NOT_EFFECTIVE|… /
    /// DPI_MISMATCH|… / INPUT_BLOCKED|… / PARSE_ERROR|…（成功与失败取证面同构）。</summary>
    public async Task<string> ClickAsync(string name, CancellationToken ct)
    {
        var core = _host.Core;
        if (core is null) return string.Empty;

        // ① 前台确权先行（先确权再测量）：rect 若在 1.5s 前台等待之前取得，会跨重排/滚动失效（ER-2）。
        var fgOk = await EnsureForegroundAsync(ForegroundTimeoutMs, ct).ConfigureAwait(false);
        if (fgOk == false)
        {
            return Report(Fail(ClickOutcome.NotEffective, name,
                "前台未确权（目标窗体未成为前台窗口，真实点击可能仅激活窗口）",
                $"target=0x{_host.HostFormHandle.ToInt64():X};fg=0x{Win32NativeInput.GetForegroundWindow().ToInt64():X};attempts=0"));
        }

        var lastOutcome = ClickOutcome.Other;
        var lastDetail = string.Empty;
        var lastEvidence = string.Empty;
        var attempts = 0;
        // E7：opId 跨 attempt 复用（迟到回执可认领）；attempt 号单独随回执返回，避免旧拍冒充本次。
        var opId = Guid.NewGuid().ToString("N");
        ClickProbe? finalProbe = null;

        while (attempts < MaxAttempts)
        {
            attempts++;
            ct.ThrowIfCancellationRequested();

            // ② 元素级探测：rect + 中心点归属（elementFromPoint）+ 滚动后复测，页面内一次完成。
            var probeText = HtmlElementNode.UnwrapString(
                await RunOnUiAsync(() => core.ExecuteScriptAsync(_executor.RenderRectQuery(name)), ct).ConfigureAwait(false));
            if (ClickOutcomes.IsTerminalText(probeText)) return Report(probeText!);
            if (string.IsNullOrWhiteSpace(probeText)) return Report($"PARSE_ERROR|target={name};detail=坐标探测返回空");
            if (!ClickProbe.TryParse(probeText, out var probe) || probe is null)
                return Report($"PARSE_ERROR|target={name};detail=坐标探测非法: {Truncate(probeText)}");

            // ③ 坐标换算（CSS px × dpr → 客户区设备像素）；dpr 越界显式红，不再静默夹紧（ER-6）。
            if (!probe.TryMapToClient(out var clientX, out var clientY, out var mapError))
                return Report(Fail(ClickOutcome.DpiMismatch, name, mapError, $"{probe.ToEvidence()};attempts={attempts}"));

            var clientPt = new Win32NativeInput.NativePoint(clientX, clientY);
            var screenPt = clientPt;
            if (_host.IsHandleCreated)
                Win32NativeInput.ClientToScreen(_host.Handle, ref screenPt);

            var dpi = _host.IsHandleCreated ? Win32NativeInput.GetDpiForWindow(_host.Handle) : 0u;
            var fg = Win32NativeInput.GetForegroundWindow();
            var hitBefore = Win32NativeInput.WindowFromPoint(screenPt);
            var contentHit = IsContentChild(hitBefore);
            if (!contentHit && IsWithinHostWindow(hitBefore))
            {
                // 刚强制前台 / 布局未落定时 WindowFromPoint 可能返回宿主窗体本身（无子窗口命中）：
                // 让路一拍复取，避免把「瞬时几何」误判成遮挡（实测 23:53 假 OCCLUDED 即此因）。
                await Task.Delay(120, ct).ConfigureAwait(false);
                hitBefore = Win32NativeInput.WindowFromPoint(screenPt);
                contentHit = IsContentChild(hitBefore);
            }
            var evidence =
                $"{probe.ToEvidence()};dpi={dpi};dpiConsistent={probe.IsDpiConsistent(dpi)};"
                + $"client=({clientPt.X},{clientPt.Y});screen=({screenPt.X},{screenPt.Y});"
                + $"fg=0x{fg.ToInt64():X};hit=0x{hitBefore.ToInt64():X};contentHit={contentHit}";

            // ④ 元素级归属：被遮挡/视口外不发送（发出去就是点别的元素，并掩盖根因）。
            var verdict = probe.Verdict;
            if (verdict == ClickOutcome.Occluded)
            {
                lastOutcome = ClickOutcome.Occluded;
                lastDetail = "点击中心被其它元素占用（elementFromPoint 不归属目标；scrollIntoView 后仍不归属）";
                lastEvidence = evidence;
                break;
            }
            if (verdict == ClickOutcome.OffViewport)
            {
                lastOutcome = ClickOutcome.OffViewport;
                lastDetail = "目标中心不在视口内（scrollIntoView 后仍不可见）";
                lastEvidence = evidence;
                break;
            }

            // ⑤ 悬停收敛（ER-2 复验）：先把指针移到目标触发 hover（hover 可能改布局/位移），
            //    再现测一次；位移 >1px 则跟随（≤2 次），使「探测点 = 按下点」。实测 00:06 轮
            //    probe 归属按钮、回执却落到父容器 region-seg，即 hover/重排造成的落点漂移。
            var hoverTrace = "none";
            MoveTo(screenPt);
            await Task.Delay(HoverSettleMs, ct).ConfigureAwait(false);
            var hoverAborted = false;
            for (var i = 0; i < MaxHoverCorrections; i++)
            {
                var reText = HtmlElementNode.UnwrapString(
                    await RunOnUiAsync(() => core.ExecuteScriptAsync(_executor.RenderRectQuery(name)), ct).ConfigureAwait(false));
                if (!ClickProbe.TryParse(reText, out var reprobe) || reprobe is null)
                {
                    hoverTrace = $"unparsable@{i}";
                    break;
                }
                finalProbe = reprobe;
                if (reprobe.Verdict is ClickOutcome.Occluded or ClickOutcome.OffViewport)
                {
                    lastOutcome = reprobe.Verdict;
                    lastDetail = "悬停后目标不再归属（hover 位移/遮挡）";
                    lastEvidence = $"{evidence};hover={hoverTrace};hoverProbe={reprobe.ToEvidence()}";
                    hoverAborted = true;
                    break;
                }
                if (!reprobe.TryMapToClient(out var nx, out var ny, out _))
                {
                    hoverTrace = $"dpr-invalid@{i}";
                    break;
                }
                var next = new Win32NativeInput.NativePoint(nx, ny);
                if (_host.IsHandleCreated)
                    Win32NativeInput.ClientToScreen(_host.Handle, ref next);
                var dx = next.X - screenPt.X;
                var dy = next.Y - screenPt.Y;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                {
                    hoverTrace = $"stable@{i}";
                    break;
                }
                clientPt = new Win32NativeInput.NativePoint(nx, ny);
                screenPt = next;
                hoverTrace = $"moved@{i}({dx:+0;-0},{dy:+0;-0})";
                MoveTo(screenPt);
                await Task.Delay(HoverSettleMs / 2, ct).ConfigureAwait(false);
            }
            if (hoverAborted) break;

            // 落点窗口归属按**最终**落点复算（悬停可能位移），并把终值写回证据。
            hitBefore = Win32NativeInput.WindowFromPoint(screenPt);
            contentHit = IsContentChild(hitBefore);
            evidence =
                $"{probe.ToEvidence()};dpi={dpi};dpiConsistent={probe.IsDpiConsistent(dpi)};"
                + $"client=({clientPt.X},{clientPt.Y});screen=({screenPt.X},{screenPt.Y});"
                + $"fg=0x{fg.ToInt64():X};hit=0x{hitBefore.ToInt64():X};contentHit={contentHit};hover={hoverTrace}";

            // ⑥ 回执令牌 + 落点窗口归属（须落到控件内容窗口，不只校验到宿主窗体）。
            finalProbe ??= probe;
            await RunOnUiAsync(() => core.ExecuteScriptAsync(_executor.RenderClickAckInstall(opId, attempts)), ct).ConfigureAwait(false);

            if (!IsWithinHostWindow(hitBefore))
            {
                lastOutcome = ClickOutcome.Occluded;
                lastDetail = "坐标落点不在宿主窗体（被其它窗口遮挡）";
                lastEvidence = evidence;
                break;
            }
            if (hitBefore != IntPtr.Zero)
                Win32NativeInput.SetFocus(hitBefore);

            // ⑦ 发送：指针已收敛到最终落点（再 move 一次确保命中测试刷新），稍候再 down/up。
            MoveTo(screenPt);
            await Task.Delay(60, ct).ConfigureAwait(false);
            if (!Win32NativeInput.SendMouse(Win32NativeInput.MOUSEEVENTF_LEFTDOWN, 0, 0))
            {
                return Report(Fail(ClickOutcome.InputBlocked, name,
                    "输入被系统拦截（SendInput=0，疑似 UIPI/会话隔离）", $"{evidence};attempts={attempts}"));
            }
            await Task.Delay(50, ct).ConfigureAwait(false);
            // E8：down/up 同元素不变量——UP 前复测一次归属；不一致仍发送 UP（避免按键悬挂），但分型 SPLIT。
            var upStable = true;
            var upText = HtmlElementNode.UnwrapString(
                await RunOnUiAsync(() => core.ExecuteScriptAsync(_executor.RenderRectQuery(name)), ct).ConfigureAwait(false));
            if (ClickProbe.TryParse(upText, out var upProbe) && upProbe is not null)
                upStable = upProbe.Verdict == ClickOutcome.Ok;
            Win32NativeInput.SendMouse(Win32NativeInput.MOUSEEVENTF_LEFTUP, 0, 0);

            // ⑧ 送达判定：回执按 opId 令牌匹配本次点击（等待语义=C# 有界轮询；页面侧只返回同步快照）。
            var (ack, ackRaw) = await WaitForAckAsync(core, opId, name, attempts, AckTimeoutMs, ct).ConfigureAwait(false);
            var ackEvidence = ack is not null ? ack.ToEvidence() : $"ack=-;ackRaw={Truncate(ackRaw, 120)}";

            if (ack is not null && IsAckFor(ack, opId, name, attempts))
            {
                var geometryOk = finalProbe is null
                    || ack.RectMatches(finalProbe.X, finalProbe.Y, finalProbe.Width, finalProbe.Height);
                if (!upStable)
                {
                    return Report(Fail(ClickOutcome.Split, name,
                        "按下/抬起之间目标归属发生变化（down/up 不同元素）",
                        $"{evidence};{ackEvidence};attempts={attempts};opId={opId}"));
                }
                if (!geometryOk)
                {
                    return Report(Fail(ClickOutcome.Stale, name,
                        "回执矩形与点击前探测矩形不一致（落点已漂移）",
                        $"{evidence};{ackEvidence};attempts={attempts};opId={opId}"));
                }
                return Report(Format(ClickOutcome.Ok, name, null,
                    $"{evidence};{ackEvidence};attempts={attempts};opId={opId}"));
            }

            // ⑨ 迟到回执复核（同令牌 + 同 attempt）：窗口抖动导致的迟到送达不再触发重发（防双触发）。
            var (late, lateRaw) = await WaitForAckAsync(core, opId, name, attempts, LateAckTimeoutMs, ct, initialDelay: false).ConfigureAwait(false);
            if (late is not null && IsAckFor(late, opId, name, attempts))
            {
                return Report(Format(ClickOutcome.Ok, name, null,
                    $"{evidence};{late.ToEvidence()};attempts={attempts};lateAck=true;opId={opId}"));
            }
            if (ack is null && late is not null) ackEvidence = $"ack=-;ackRaw={Truncate(lateRaw, 120)}";

            lastOutcome = ClickOutcome.NotEffective;
            lastDetail = ack is null
                ? "点击未被页面接收（DOM 未产生 click：输入已发送但未被派发）"
                : $"点击落到其它元素（DOM click 命中 '{ack.Name ?? string.Empty}'，期望 '{name}'）";
            lastEvidence = $"{evidence};{ackEvidence};opId={opId}";

            // ⑩ 仅 NOT_EFFECTIVE 允许一次「重取 rect + 重发」；NOT_FOUND/NOT_VISIBLE/OCCLUDED 不重试。
        }

        return Report(Fail(lastOutcome, name, lastDetail, $"{lastEvidence};attempts={attempts}"));
    }

    /// <summary>屏幕坐标 → 虚拟桌面归一绝对坐标（MOUSEEVENTF_VIRTUALDESK 语义；多屏拼接原点偏移在此单点处理）。</summary>
    private static (int X, int Y) ToAbsolute(Win32NativeInput.NativePoint screenPt)
    {
        var vx = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_XVIRTUALSCREEN);
        var vy = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_YVIRTUALSCREEN);
        var vw = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_CXVIRTUALSCREEN);
        var vh = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_CYVIRTUALSCREEN);
        return ((int)((long)(screenPt.X - vx) * 65535 / Math.Max(1, vw - 1)),
                (int)((long)(screenPt.Y - vy) * 65535 / Math.Max(1, vh - 1)));
    }

    /// <summary>移动指针到屏幕坐标（真实输入 move；Chromium 命中测试需先经过该点）。</summary>
    private static void MoveTo(Win32NativeInput.NativePoint screenPt)
    {
        var (x, y) = ToAbsolute(screenPt);
        Win32NativeInput.SendMouse(
            Win32NativeInput.MOUSEEVENTF_ABSOLUTE | Win32NativeInput.MOUSEEVENTF_VIRTUALDESK | Win32NativeInput.MOUSEEVENTF_MOVE,
            x, y);
    }

    /// <summary>落点是否落在控件内容窗口内（hit = 控件句柄或其子窗口）。无句柄时无法判定 → false（仅取证，不据此判红）。</summary>
    private bool IsContentChild(IntPtr hit)
    {
        if (hit == IntPtr.Zero || !_host.IsHandleCreated) return false;
        var handle = _host.Handle;
        if (handle == IntPtr.Zero) return false;
        return hit == handle || Win32NativeInput.IsChild(handle, hit);
    }

    /// <summary>落点是否仍在本宿主窗体内（窗体自身 / 其任意子窗口 / 被拥有的顶层窗口）。
    /// 仅用于拦截「点到别的程序」；同窗体兄弟控件与瞬时几何**不**在此判红——最终送达由 DOM 回执裁决，
    /// 窗口级预过滤只管「窗口归属」，不管「元素归属」（后者由 elementFromPoint 探针承担）。</summary>
    private bool IsWithinHostWindow(IntPtr hit)
    {
        if (hit == IntPtr.Zero) return true;                 // 无法判定 → 不拦（交回执裁决）
        if (!_host.IsHandleCreated) return true;
        var form = _host.HostFormHandle;
        var control = _host.Handle;
        if (form == IntPtr.Zero)
            return control == IntPtr.Zero || hit == control || Win32NativeInput.IsChild(control, hit);
        if (hit == form || hit == control) return true;
        if (Win32NativeInput.IsChild(form, hit)) return true;
        return Win32NativeInput.GetAncestor(hit, Win32NativeInput.GA_ROOT) == form;
    }

    /// <summary>成功/失败同构的取证行：前缀 + target + 可选 detail + 证据串。</summary>
    private static string Format(ClickOutcome outcome, string name, string? detail, string evidence)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(ClickOutcomes.Prefix(outcome));
        sb.Append("|target=").Append(name);
        if (!string.IsNullOrEmpty(detail)) sb.Append(";detail=").Append(detail);
        if (!string.IsNullOrEmpty(evidence)) sb.Append(';').Append(evidence);
        return sb.ToString();
    }

    private static string Fail(ClickOutcome outcome, string name, string detail, string evidence)
        => Format(outcome, name, detail, evidence);

    /// <summary>唯一诊断出口（成功与失败都记一条，便于单行 grep 复算）。</summary>
    private string Report(string line)
    {
        _diagnostics.Report("HtmlChannel_realclick", line);
        return line;
    }

    private static string Truncate(string? text, int max = 240)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text.Substring(0, max) + "…";


    /// <summary>有界等待回执：命中（opId + name 双匹配）即返回；超时返回最后一次可解析回执与原始文本（供分型/取证）。
    /// 等待语义必须在 C# 侧——WebView2 对页面返回的 Promise 会序列化为 <c>{}</c>（实测：回读立即返回空对象）。</summary>
    private async Task<(ClickAck? Ack, string Raw)> WaitForAckAsync(
        CoreWebView2 core,
        string opId,
        string name,
        int attempt,
        int timeoutMs,
        CancellationToken ct,
        bool initialDelay = true)
    {
        if (initialDelay)
            await Task.Delay(AckInitialDelayMs, ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        ClickAck? last = null;
        var raw = string.Empty;
        while (true)
        {
            raw = HtmlElementNode.UnwrapString(
                await RunOnUiAsync(() => core.ExecuteScriptAsync(_executor.RenderClickAckRead()), ct).ConfigureAwait(false));
            if (TryExtractAck(raw, out var ackJson) && ClickAck.TryParse(ackJson, out var ack) && ack is not null)
            {
                last = ack;
                if (IsAckFor(ack, opId, name, attempt))
                    return (ack, raw);
            }
            if (sw.ElapsedMilliseconds >= timeoutMs)
                return (last, raw);
            await Task.Delay(AckPollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>送达判据（唯一）：同 opId + 同目标 + 同 attempt；三缺一不认。</summary>
    private static bool IsAckFor(ClickAck ack, string opId, string name, int attempt)
        => ack.Matches(opId)
           && ack.MatchesAttempt(attempt)
           && string.Equals(ack.Name, name, StringComparison.Ordinal);

    /// <summary>回执外层信封 → 内层回执原文：<c>{installed,op,ack}</c>；<c>ack</c> 非对象时返回 false（保留原文供取证）。</summary>
    private static bool TryExtractAck(string raw, out string ackJson)
    {
        ackJson = raw;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("ack", out var ack) || ack.ValueKind != JsonValueKind.Object) return false;
            ackJson = ack.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>在 UI 线程执行脚本（CoreWebView2 仅限 UI 线程；本方法在首个 await 之后可能已在线程池）。</summary>
    private Task<string> RunOnUiAsync(Func<Task<string>> action, CancellationToken ct)
        => _host.InvokeOnUiAsync(_ => new ValueTask<string>(action()), ct);

    /// <summary>激活宿主窗体并聚焦控件，随后轮询等待前台确权（非阻塞 Task.Delay，保持消息泵）。
    /// true=已成前台；false=超时仍未确权；null=无法判定（无句柄/无窗体：headless/测试桩，沿用旧行为）。
    /// <para>强制路径：<see cref="Win32NativeInput.ForceForeground"/>（AttachThreadInput 绕过前台锁）——仅 <c>form.Activate()</c>
    /// 在控制台/他进程持前台时会被 Windows 拒绝，导致「首击仅激活窗口」。</para></summary>
    private async Task<bool?> EnsureForegroundAsync(int timeoutMs, CancellationToken ct)
    {
        if (!_host.IsHandleCreated)
            return null;
        var formHandle = _host.HostFormHandle;
        if (formHandle == IntPtr.Zero)
            return null;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < Math.Max(0, timeoutMs))
        {
            _host.ActivateForInput();
            Win32NativeInput.ForceForeground(formHandle);
            if (Win32NativeInput.IsForegroundOf(formHandle))
                return true;
            await Task.Delay(40, ct).ConfigureAwait(false);
        }
        return Win32NativeInput.IsForegroundOf(formHandle);
    }
}
