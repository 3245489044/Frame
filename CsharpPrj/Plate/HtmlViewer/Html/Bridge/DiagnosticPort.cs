#nullable enable

using System;

namespace UiBackend.Html;

/// <summary>
/// 桥链路诊断出口（category, message 两元）——各桥组件（事件桥 / 脚本执行器 / 脚本宿主 / 写回运行时）
/// 共用的轻量实现，消除「Diagnostics 字段 + Report 方法」多份重复。未接线为空操作（宿主接线到日志门面）。
/// </summary>
internal sealed class DiagnosticPort
{
    /// <summary>诊断接收端口（未接线为 null）。</summary>
    public Action<string, string>? Sink;

    /// <summary>上报一条诊断（未接线时静默丢弃）。</summary>
    public void Report(string category, string message) => Sink?.Invoke(category, message);
}
