using System;
using System.Collections.Generic;

namespace Plate.HtmlViewer;

/// <summary>
/// HTML 侧 <c>postMessage</c> 的事件参数：携带原始 JSON 与已解析键值字典负载。
/// 消费方（ECS 事件绑定 / 函数分派）用它读取 <c>cmd</c>/<c>session</c>/<c>round</c> 等字段。
/// </summary>
public sealed class HtmlMessageReceivedEventArgs : EventArgs
{
    public HtmlMessageReceivedEventArgs(string json, IReadOnlyDictionary<string, string>? payload)
    {
        Json = json;
        Payload = payload;
    }

    /// <summary>HTML postMessage 的原始 JSON 字符串。</summary>
    public string Json { get; }

    /// <summary>解析后的键值负载；非对象 JSON / 解析失败为 null。</summary>
    public IReadOnlyDictionary<string, string>? Payload { get; }

    /// <summary>按键取负载字段；无该键或负载为 null 返回 null。</summary>
    public string? Get(string key)
        => Payload is not null && Payload.TryGetValue(key, out var v) ? v : null;
}
