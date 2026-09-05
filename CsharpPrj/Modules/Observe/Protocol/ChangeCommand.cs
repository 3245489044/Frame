using System.Text.Json.Serialization;

namespace Modules.Observe;

/// <summary>变更命令的非泛型基类（用于传输层按命令类型识别，不承载负载）。</summary>
public abstract record ChangeCommand(string ChangeId, string Key, long Version) : Message(ChangeId)
{
    /// <summary>负载的弱类型视图（供传输层读取，如 WebView2Transport 取 HTML 字符串）；泛型强类型负载见 <see cref="ChangeCommand{T}.Payload"/>。</summary>
    [JsonIgnore]
    public abstract object? PayloadObject { get; }
}

/// <summary>变更命令：Producer(A) → Consumer(B)。<c>Version</c> 单调递增，供 B 侧幂等与读方陈旧检测。</summary>
public sealed record ChangeCommand<T>(string ChangeId, string Key, long Version, T Payload) : ChangeCommand(ChangeId, Key, Version)
{
    public override object? PayloadObject => Payload;
}

