using System;

namespace Modules.Observe;

/// <summary>写方选项。<c>WindowTimeout</c> 为收敛窗口上界（null 用默认）；<c>MaxRetries</c> 为发送/降级后重试次数。</summary>
public sealed record WriteOptions(
    BarrierMode Barrier = BarrierMode.Timeout,
    TimeSpan? WindowTimeout = null,
    int MaxRetries = 0);
