using System.Text;
using Modules.Sync.Contracts;

namespace Modules.Sync.Transport;

/// <summary>
/// 握手窗口校验：协议版本 + 规则集哈希；不做认证、加密、多版本兼容。
/// <para>两侧入口分开是因为契约消息本身如此分工：<see cref="Hello"/> 由客户端发出、只带版本；
/// <see cref="Welcome"/> 由权威端发出、才带 <c>RulesetHash</c>。因此
/// 服务端在 <see cref="EvaluateHello"/> 校验版本与载荷形状，客户端在 <see cref="EvaluateWelcome"/> 校验版本与规则集哈希。</para>
/// <para>返回 <c>null</c> = 通过；否则返回值即契约 <see cref="RejectReason"/>，拒绝路径不静默。
/// 优先级固定 <b>Malformed → VersionMismatch → RulesetMismatch</b>。</para>
/// </summary>
public sealed class SyncHandshakeGate
{
    private readonly ulong _expectedRulesetHash;

    /// <param name="expectedRulesetHash">权威端规则集哈希（契约要求由规则表本身算出，不得写死常量）。</param>
    public SyncHandshakeGate(ulong expectedRulesetHash)
    {
        _expectedRulesetHash = expectedRulesetHash;
    }

    /// <summary>服务端入口：校验 <see cref="Hello"/>（版本 + 载荷形状）。</summary>
    public RejectReason? EvaluateHello(Hello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);

        if (hello.ClientBuild is null
            || Encoding.UTF8.GetByteCount(hello.ClientBuild) > SyncMessageLayout.ClientBuildMaxBytes)
        {
            return RejectReason.Malformed;
        }

        if (hello.ProtocolVersion < 0)
        {
            return RejectReason.Malformed;
        }

        if (hello.ProtocolVersion != SyncProtocol.ProtocolVersion)
        {
            return RejectReason.VersionMismatch;
        }

        return null;
    }

    /// <summary>客户端入口：校验 <see cref="Welcome"/>（版本 + 规则集哈希）。</summary>
    public RejectReason? EvaluateWelcome(Welcome welcome)
    {
        ArgumentNullException.ThrowIfNull(welcome);

        if (welcome.ProtocolVersion < 0)
        {
            return RejectReason.Malformed;
        }

        if (welcome.ProtocolVersion != SyncProtocol.ProtocolVersion)
        {
            return RejectReason.VersionMismatch;
        }

        if (welcome.RulesetHash != _expectedRulesetHash)
        {
            return RejectReason.RulesetMismatch;
        }

        return null;
    }
}
