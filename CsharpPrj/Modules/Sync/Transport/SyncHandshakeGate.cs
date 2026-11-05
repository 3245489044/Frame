using System;
using System.Collections.Generic;

namespace Modules.Sync.Transport;

/// <summary>握手裁决：接受之外的每个值都是一条明确的拒绝原因（对应契约的拒绝路径，不静默失败）。</summary>
public enum SyncHandshakeOutcome
{
    /// <summary>版本与规则集哈希均匹配：可建立会话。</summary>
    Accepted = 0,

    /// <summary>协议版本不匹配（拒绝，不建会话）。</summary>
    VersionMismatch = 1,

    /// <summary>规则集哈希不匹配（拒绝，不建会话）：两端规则集不是同一份契约。</summary>
    RuleSetMismatch = 2,

    /// <summary>握手载荷非法（例如负版本号），无法进入比较。</summary>
    Malformed = 3,
}

/// <summary>
/// 握手窗口校验：只做两件事——协议版本比较、规则集哈希比较；不做认证、加密、多版本兼容。
/// <para>判定优先级固定为 <b>Malformed → VersionMismatch → RuleSetMismatch</b>：两者都不匹配时先报版本，
/// 让“版本不符”这条更基础的原因可见。</para>
/// <para>[契约依赖 · 等待 SY-00] <c>ProtocolVersion</c> 常量与 <c>RulesetHash</c> 字段类型由契约提供；
/// 本类以构造参数接收期望值、以泛型接收哈希值类型，<b>不定义版本号、不定义哈希函数、不定义消息类型</b>。</para>
/// </summary>
/// <typeparam name="TRuleSetHash">契约的规则集哈希值类型（本层只做相等比较，不做计算）。</typeparam>
public sealed class SyncHandshakeGate<TRuleSetHash> where TRuleSetHash : IEquatable<TRuleSetHash>
{
    private readonly int _expectedProtocolVersion;
    private readonly TRuleSetHash _expectedRuleSetHash;

    /// <param name="expectedProtocolVersion">权威端期望的协议版本；契约落地后由 <c>ProtocolVersion</c> 传入。</param>
    /// <param name="expectedRuleSetHash">权威端规则集哈希；契约落地后由规则表本身算出并注入。</param>
    public SyncHandshakeGate(int expectedProtocolVersion, TRuleSetHash expectedRuleSetHash)
    {
        if (expectedProtocolVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedProtocolVersion), "协议版本不能为负。");
        }

        _expectedProtocolVersion = expectedProtocolVersion;
        _expectedRuleSetHash = expectedRuleSetHash;
    }

    /// <summary>期望的协议版本。</summary>
    public int ExpectedProtocolVersion => _expectedProtocolVersion;

    /// <summary>判定一次 Hello 携带的版本与规则集哈希。</summary>
    public SyncHandshakeOutcome Evaluate(int protocolVersion, TRuleSetHash ruleSetHash)
    {
        if (protocolVersion < 0)
        {
            return SyncHandshakeOutcome.Malformed;
        }

        if (protocolVersion != _expectedProtocolVersion)
        {
            return SyncHandshakeOutcome.VersionMismatch;
        }

        if (!EqualityComparer<TRuleSetHash>.Default.Equals(ruleSetHash, _expectedRuleSetHash))
        {
            return SyncHandshakeOutcome.RuleSetMismatch;
        }

        return SyncHandshakeOutcome.Accepted;
    }
}
