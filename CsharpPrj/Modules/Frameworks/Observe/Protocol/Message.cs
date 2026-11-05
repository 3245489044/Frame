using System.Text.Json.Serialization;

namespace Modules.Observe;

/// <summary>协议消息基类。每个消息携带 <c>ChangeId</c> 关联到一次变更（ack 链据此归属会话）。</summary>
[JsonDerivedType(typeof(Ack), "ack")]
public abstract record Message(string ChangeId);
