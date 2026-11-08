#nullable enable

using System;

namespace Share.SurfacePaint.Compat;

/// <summary>
/// 兼容层日志端口（宿主装配期注入一次）。
/// <para>使下沉到 Share 的兼容组件（诊断/字体）不再反向引用 FrontEndDriven.Diagnostics。</para>
/// </summary>
public static class SurfaceCompatLog
{
    /// <summary>日志接收端（null ⇒ 丢弃；宿主启动时注入 <c>FrontEndDrivenLog.Log</c>）。</summary>
    public static Action<string, object?>? Sink { get; set; }

    /// <summary>写一条结构化事件。</summary>
    /// <param name="eventId">事件 id。</param>
    /// <param name="data">载荷（可空）。</param>
    public static void Log(string eventId, object? data = null) => Sink?.Invoke(eventId, data);
}

/// <summary>
/// 字体缺省（宿主装配期注入一次）：声明候选全部不可用时的回退族。
/// </summary>
public static class SurfaceFontDefaults
{
    /// <summary>回退字体族（默认与 Plate.TConfig StyleConfig 一致）。</summary>
    public static string FallbackFamily { get; set; } = "Microsoft YaHei UI";
}
