using System.Diagnostics;

namespace Operation.ReconcileDriven.Diagnostics;

/// <summary>对账诊断出口（轻量；可被宿主日志器接管）。</summary>
public static class ReconcileLog
{
    /// <summary>诊断钩子（宿主注入日志器）；null = 静默。</summary>
    public static Action<string, object?>? Sink { get; set; }

    public static void Log(string code, object? payload = null)
    {
        Debug.WriteLine($"[reconcile] {code} {payload}");
        try { Sink?.Invoke(code, payload); } catch { /* 诊断失败不影响主路径 */ }
    }
}
