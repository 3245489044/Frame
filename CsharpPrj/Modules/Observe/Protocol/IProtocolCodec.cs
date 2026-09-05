namespace Modules.Observe;

/// <summary>协议编解码（跨进程通道用；进程内传输可直接传对象不经编解码）。</summary>
public interface IProtocolCodec
{
    Message? Decode(string wire);
    string Encode(Message message);
}
