using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 传输层抽象（收敛层与具体通道之间唯一边界）：只负责消息收发，不感知业务逻辑。实现须声明自己的收敛证据策略
/// <see cref="EvidenceStrategy"/>（决定「收敛」是事实（回调/快照）还是逼近（Probe 轮询））。实现例：WebView2Transport（真实 UI）、
/// InMemoryTransport（单测自环）。
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>投递一条协议消息到对端。</summary>
    Task SendAsync(Message message, CancellationToken ct = default);

    /// <summary>订阅来自对端的消息（ack / 快照）。返回的 <see cref="IDisposable"/> 用于退订。</summary>
    IDisposable Subscribe(Func<Message, CancellationToken, Task> handler);

    /// <summary>本传输的收敛证据能力声明。</summary>
    EvidenceStrategy EvidenceStrategy { get; }
}
