#nullable enable

using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// 真实鼠标点击执行器（Win32 SendInput，isTrusted=true）——经 JS 读元素中心 → CSS 像素 × DPI 换算客户区设备像素 →
/// ClientToScreen 转屏幕坐标 → SendInput 发送真实左键移动+按下+抬起。与合成 dispatchEvent（isTrusted=false）不同，
/// 走完整真实输入管线（命中测试/焦点/事件冒泡）。产生副作用前先等前台确权（消除「首击仅激活窗口」丢失）。
/// </summary>
public sealed class RealInputClicker
{
    /// <summary>真实点击「前台确权」等待上限（毫秒）。Windows 前台锁下激活可能需多次重试。</summary>
    private const int ForegroundTimeoutMs = 1500;

    private readonly IWebViewHost _host;
    private readonly HtmlScriptExecutor _executor;
    private readonly DiagnosticPort _diagnostics = new();

    public RealInputClicker(IWebViewHost host, HtmlScriptExecutor executor, Action<string, string>? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _diagnostics.Sink = diagnostics;
    }

    /// <summary>对具名元素执行真实点击（须在 UI 线程调用）。返回 OK|… / NOT_FOUND|… / NOT_VISIBLE / NOT_EFFECTIVE|… / PARSE_ERROR。</summary>
    public async Task<string> ClickAsync(string name, CancellationToken ct)
    {
        var core = _host.Core;
        if (core is null) return string.Empty;

        // 坐标查询模板（参数 JSON 序列化注入，防引号转义漂移）；NOT_FOUND 附带文档诊断后缀。
        var raw = await core.ExecuteScriptAsync(_executor.RenderRectQuery(name)).ConfigureAwait(false);
        // ExecuteScriptAsync 返回 JSON 字符串字面量（"..."），解包后再解析对象
        var json = HtmlElementNode.UnwrapString(raw);
        if (json == "NOT_VISIBLE")
            return json;
        if (json is not null && json.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return json; // 含诊断后缀（names/body/state/href）
        if (string.IsNullOrWhiteSpace(json))
            return json;

        double cx, cy, dpr;
        try
        {
            using var doc = JsonDocument.Parse(json);
            cx = doc.RootElement.GetProperty("x").GetDouble();
            cy = doc.RootElement.GetProperty("y").GetDouble();
            dpr = doc.RootElement.GetProperty("dpr").GetDouble();
        }
        catch (JsonException)
        {
            return "PARSE_ERROR";
        }

        // ① 激活/聚焦宿主窗体与控件并等待前台确权：SendInput 需目标窗口命中；未激活时 Chromium 把首个点击仅用于激活窗口，
        //    不产生 click（首击丢失）。等待前台再点击使首个真实点击即生效；无法确权 → NOT_EFFECTIVE（不假阳）。
        var fgOk = await EnsureForegroundAsync(ForegroundTimeoutMs, ct).ConfigureAwait(false);
        if (fgOk == false)
            return $"NOT_EFFECTIVE|前台未确权（目标窗体未成为前台窗口，真实点击可能仅激活窗口）；target=0x{_host.HostFormHandle.ToInt64():X}, fg=0x{Win32NativeInput.GetForegroundWindow().ToInt64():X}";

        // ② 精确坐标：设备像素 = CSS 像素 × devicePixelRatio（WebView2 真实 DPI/缩放，优于 DeviceDpi 近似）；
        //    viewport 原点 = 控件客户区原点，再 ClientToScreen 转屏幕坐标。
        var scale = Math.Max(0.25, dpr);
        var clientPt = new Win32NativeInput.NativePoint((int)(cx * scale), (int)(cy * scale));
        var screenPt = clientPt;
        if (_host.IsHandleCreated)
            Win32NativeInput.ClientToScreen(_host.Handle, ref screenPt);

        // ③ 虚拟桌面归一（E-8）：绝对坐标按虚拟屏（含原点偏移 vx/vy）归一 + MOUSEEVENTF_VIRTUALDESK——
        //    旧实现按主屏 SM_CXSCREEN/SM_CYSCREEN 归一，窗口落在非主屏/多屏拼接处时坐标偏移 ⇒ 真实点击打偏。
        var vx = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_XVIRTUALSCREEN);
        var vy = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_YVIRTUALSCREEN);
        var vw = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_CXVIRTUALSCREEN);
        var vh = Win32NativeInput.GetSystemMetrics(Win32NativeInput.SM_CYVIRTUALSCREEN);
        var absX = (int)((long)(screenPt.X - vx) * 65535 / Math.Max(1, vw - 1));
        var absY = (int)((long)(screenPt.Y - vy) * 65535 / Math.Max(1, vh - 1));

        // ④ Chromium 输入管线需先命中（move，给足延时让 hover/命中测试完成），再 down/up 才产生真实 click。
        //    用 Task.Delay 而非 Thread.Sleep：本方法已在 UI 线程执行，Thread.Sleep 会阻塞整条 UI 线程。
        Win32NativeInput.SendMouse(
            Win32NativeInput.MOUSEEVENTF_ABSOLUTE | Win32NativeInput.MOUSEEVENTF_VIRTUALDESK | Win32NativeInput.MOUSEEVENTF_MOVE, absX, absY);
        await Task.Delay(80, ct).ConfigureAwait(false);
        Win32NativeInput.SendMouse(Win32NativeInput.MOUSEEVENTF_LEFTDOWN, 0, 0);
        await Task.Delay(50, ct).ConfigureAwait(false);
        Win32NativeInput.SendMouse(Win32NativeInput.MOUSEEVENTF_LEFTUP, 0, 0);

        // ⑤ 坐标回执取证（IC-1）：一条结构化诊断回答「点击坐标落在哪个窗口/控件」——hit 非 WebView2 内容 ⇒ 判坐标偏移；
        //    fg 非本窗体 ⇒ 判前台缺失。回执并入返回串（前缀仍是 `OK`，保持既有断言兼容）。
        var fg = Win32NativeInput.GetForegroundWindow();
        var hit = Win32NativeInput.WindowFromPoint(screenPt);
        _diagnostics.Report("HtmlChannel_realclick",
            $"realclick target={name} rect=({cx:F0},{cy:F0}) dpr={dpr:F2} client=({clientPt.X},{clientPt.Y}) "
            + $"screen=({screenPt.X},{screenPt.Y}) abs=({absX},{absY}) virtual=({vx},{vy},{vw},{vh}) "
            + $"fg=0x{fg.ToInt64():X} hit=0x{hit.ToInt64():X}");
        return $"OK|client=({clientPt.X},{clientPt.Y});screen=({screenPt.X},{screenPt.Y});fg=0x{fg.ToInt64():X};hit=0x{hit.ToInt64():X}";
    }

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
