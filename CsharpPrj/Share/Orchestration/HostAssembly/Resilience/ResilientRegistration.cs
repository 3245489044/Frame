using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Resilience;

/// <summary>
/// 弹性注册单元 —— 一个「允许失败但须可观测」的可选装配注册项。
/// 失败不阻断宿主，但会记入降级清单并触发 <see cref="OnDegrade"/> 回调（如写诊断日志）。
/// </summary>
public sealed record ResilientRegistration(
    string ModuleName,
    Func<CancellationToken, Task> Register,
    Action<string, Exception>? OnDegrade = null);
