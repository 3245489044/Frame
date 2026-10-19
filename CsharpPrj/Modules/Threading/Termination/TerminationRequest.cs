using System;

namespace Modules.Threading;

/// <summary>
/// 一次终止请求的结构化证据（唯一可审计面）：作用域 + 理由 + 证据 + 时刻 + 已运行时长。
/// <see cref="Forced"/> 表达「由框架强制退出」而非被取消方自停。
/// </summary>
public sealed record TerminationRequest(
    string ScopeId,
    string Owner,
    TerminationReason Reason,
    string? Evidence,
    DateTimeOffset At,
    TimeSpan Elapsed,
    bool Forced);
