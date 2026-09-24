#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Plate.HtmlViewer;

namespace UiBackend.Html;

/// <summary>
/// WinForms 宿主 ↔ HTML 页面的统一传递通道（唯一端口）——把「整页渲染 + 页内 DOM 指令 + 页→宿主事件」三个面
/// 收敛到一条通道，由 <see cref="HtmlViewer"/> 实现。CoreWebView2 的访问与 UI 线程封送全部集中在本通道实现内
/// （唯一封送点），消费方（值契约 / 事件桥 / 脚本桥 / Agent 库）不再各自持有 HtmlViewer / IDomBridge。
/// <para><b>线程封送单点契约（P4 收尾）</b>：任何 CoreWebView2 访问与 UI 线程封送都必须经本通道（<see cref="RenderHtmlAsync"/>
/// / <see cref="ExecuteAsync"/> / <see cref="IsBridgeReadyAsync"/>）；禁止消费方自持 HtmlViewer 实例并行封送或直接触碰
/// CoreWebView2，以避免多段各自封送造成的线程竞态与行为分叉。</para>
/// </summary>
public interface IHtmlChannel
{
    /// <summary>整页内容渲染（htmlviewer 宿主整页；body 片段经 HtmlDocumentRenderer 包页壳）。后台线程调用自动封送 UI 线程。</summary>
    Task RenderHtmlAsync(string html, CancellationToken ct = default);

    /// <summary>对 data-name 元素执行受控 DOM 指令。op 白名单（见 <see cref="DomOps"/>）：set/get/click/bounds/event/realclick。</summary>
    Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default);

    /// <summary>
    /// 对当前活 document 执行**原始 JS**（桥注入 / 参数化模板执行 / 就绪查询）。返回页面执行返回值原文（ExecuteScriptAsync 语义）。
    /// 调用方负责 JSON 序列化安全与注入幂等；本通道只保证「CoreWebView2 访问 + UI 线程封送」收敛到唯一实现（HtmlViewer）。
    /// </summary>
    Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default);

    /// <summary>就绪断言（P1 就绪契约）：查询当前活 document 是否已注入 <c>__domBridge</c> 与点击桥（[data-cmd]/[data-event] → postMessage）。</summary>
    Task<bool> IsBridgeReadyAsync(CancellationToken ct = default);

    /// <summary>
    /// 宿主 → 页面**反向推送**（页面侧 <c>window.chrome.webview message</c> 事件）：事件驱动的变化刷新用，
    /// 页面脚本按 <c>data-refresh-throttle</c>（默认 1000ms）做 trailing 节流后经既有消息桥回投刷新请求。
    /// 返回 false = 未送达（CoreWebView2 未就绪 / 页面脚本未注入 / 推送异常），调用方按诊断计（不得静默当成功）。
    /// </summary>
    Task<bool> PostToPageAsync(string json, CancellationToken ct = default);

    /// <summary>
    /// 回流链路自检（P0 探针）：经本通道注入 <c>__loopback__</c> postMessage，并确认 <c>MessageReceived</c>/WebView2 传输侧
    /// 真的收到。直接证明「DOM postMessage → C# 消息桥入口」是否闭环，**不依赖可选诊断 sink**（消除「无日志=未达 C#」伪证）。
    /// 返回 true=回流正常；false=传输断裂（未订阅/实例错配/超时）。
    /// </summary>
    Task<bool> LoopbackProbeAsync(CancellationToken ct = default);

    /// <summary>HTML 侧 postMessage（消息桥负载）到达宿主时触发（已在 UI 线程）。</summary>
    event EventHandler<HtmlMessageReceivedEventArgs> MessageReceived;
}