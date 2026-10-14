#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// HTML 后端就绪报告——装配后返回结构化绑定状态，替代「桥就绪=True」的布尔掩盖。
/// 各绑定项可逐一断言；缺任一绑定即写入 <see cref="Problems"/> 提供明确错误文本，不再静默。
/// 由组合根（见 <see cref="HtmlBackendRuntime"/> 的装配入口）初始化静态可判定项（函数在注册表 / output 目标），
/// 消费方随后调用 <see cref="HtmlBackendRuntime.AttachHostMessage"/> 更新消息订阅项（同一可变对象引用）。
/// </summary>
public sealed class HtmlRuntimeReadinessReport
{
    /// <summary>宿主内容写回端口（output=宿主控件名 → SetHtmlAsync）是否已注册。</summary>
    public bool HostWriterRegistered { get; set; }

    /// <summary>宿主控件 events.message 是否已订阅 MessageReceived。</summary>
    public bool MessageSubscribed { get; set; }

    /// <summary>
    /// 回流链路自检结果（P0 探针）：经通道注入 __loopback postMessage 并确认收到。
    /// true=「DOM postMessage → C# 消息桥入口」闭环正常；false=传输断裂（未订阅/实例错配/超时）。
    /// 装配路径（<c>AgentUiLibraryHtml.HtmlAttachAsync</c>）将其视为硬性必需项：仍失败即写入 <see cref="Problems"/>，
    /// 使「回流探针=缺失」不再被「已装配」掩盖（消除「桥就绪=True 但消息桥从未生效 → 点击静默无效」）。
    /// 本字段仅供探针自检结果读取/写入；装配硬性判定经 <see cref="Problems"/> 生效。
    /// </summary>
    public bool TransportLoopbackOk { get; set; }

    /// <summary>消息桥装配所绑定的通道实例标识（HtmlViewer.InstanceId；防「渲染实例 ≠ 订阅实例」错配可据此断言）。</summary>
    public string? ChannelInstanceId { get; set; }

    /// <summary>全部声明绑定函数是否都在注册库（ITrConfigRegistry.Contains）。</summary>
    public bool FunctionsInRegistry { get; set; }

    /// <summary>全部 events.output 写回目标是否在控件索引中唯一命中。</summary>
    public bool OutputTargetUnique { get; set; }

    /// <summary>就绪问题清单（任一非空即装配不完整；缺绑定即有明确错误文本）。</summary>
    public List<string> Problems { get; } = new();

    public void AddProblem(string problem)
    {
        Problems.Add(problem);
    }

    /// <summary>是否全部必需绑定就绪且无问题（就绪契约强制：任何缺项/问题 → 不视为「已装配」，拒绝掩盖）。</summary>
    public bool IsFullyReady
        => Problems.Count == 0
           && HostWriterRegistered && MessageSubscribed
           && FunctionsInRegistry && OutputTargetUnique;

    /// <summary>一句话就绪摘要（供宿主日志/返回文本，不再以「桥就绪=True」掩盖缺绑定）。</summary>
    public string Summary()
    {
        var parts = new List<string>
        {
            $"hostWriter={Flag(HostWriterRegistered)}",
            $"message订阅={Flag(MessageSubscribed)}",
            $"函数在注册表={Flag(FunctionsInRegistry)}",
            $"output目标唯一={Flag(OutputTargetUnique)}",
            $"回流探针={Flag(TransportLoopbackOk)}",
        };
        if (!string.IsNullOrWhiteSpace(ChannelInstanceId))
            parts.Add($"实例={ChannelInstanceId}");
        if (Problems.Count > 0)
            parts.Add("问题=[" + string.Join("; ", Problems) + "]");
        return string.Join(", ", parts);
    }

    private static string Flag(bool value) => value ? "OK" : "缺失";
}