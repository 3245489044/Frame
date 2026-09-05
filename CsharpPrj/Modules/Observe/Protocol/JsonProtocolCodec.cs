using System;
using System.Text.Json;

namespace Modules.Observe;

/// <summary>
/// 默认 JSON 编解码：用「类型全名 + 负载 JSON」的包封保证多态消息（Ack/ChangeCommand 等）可完整往返，
/// 不受基类多态序列化对泛型的限制。
/// </summary>
public sealed class JsonProtocolCodec : IProtocolCodec
{
    private sealed record WireEnvelope(string Type, string Json);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Encode(Message message)
    {
        var wire = new WireEnvelope(message.GetType().AssemblyQualifiedName!, JsonSerializer.Serialize(message, message.GetType(), Options));
        return JsonSerializer.Serialize(wire, Options);
    }

    public Message? Decode(string wire)
    {
        var envelope = JsonSerializer.Deserialize<WireEnvelope>(wire, Options);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
            return null;
        var type = Type.GetType(envelope.Type);
        if (type is null)
            return null;
        return JsonSerializer.Deserialize(envelope.Json, type, Options) as Message;
    }
}
