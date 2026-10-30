#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Strategy.Refresh.Core;

/// <summary>
/// 页面消息 → 命令队列入队器（通用件，唯一实现）—— UI 线程只做「解析 → 入队（O(1)）」，同键 latest-wins 合并，
/// 单 worker 串行执行；生命周期（停止接收 / 取消 / 释放）全部由 <see cref="UiCommandQueue"/> 单点承担。
/// <para>解析与优先级均为注入委托：本层不认识任何消息协议或领域命令词。</para>
/// </summary>
public sealed class MessageCommandScheduler : IDisposable, IAsyncDisposable
{
    /// <summary>命令身份（合并键 + 优先级分类键）。</summary>
    public readonly record struct CommandIdentity(string MergeKey, string Kind);

    private readonly UiCommandQueue _queue;
    private readonly Func<string, CommandIdentity?> _parse;
    private readonly Func<string, UiCommandPriority> _priorityOf;
    private readonly string _source;
    private long _generation;
    private int _disposed;

    /// <param name="parse">负载 → 命令身份（返回 null = 不入队，例如必须同步回投的消息）。</param>
    /// <param name="queue">命令队列（缺省进程内单例队列；注入便于测试与指标读取）。</param>
    /// <param name="priorityOf">分类键 → 优先级（缺省 Interactive：未知命令不得被后台合并/延迟）。</param>
    /// <param name="source">来源标签（审计/诊断用）。</param>
    public MessageCommandScheduler(
        Func<string, CommandIdentity?> parse,
        UiCommandQueue? queue = null,
        Func<string, UiCommandPriority>? priorityOf = null,
        string source = "webmessage")
    {
        _parse = parse ?? throw new ArgumentNullException(nameof(parse));
        _queue = queue ?? new UiCommandQueue();
        _priorityOf = priorityOf ?? (static _ => UiCommandPriority.Interactive);
        _source = string.IsNullOrWhiteSpace(source) ? "webmessage" : source;
    }

    /// <summary>底层队列（指标/诊断只读）。</summary>
    public UiCommandQueue Queue => _queue;

    /// <summary>尝试入队；false = 未入队（空负载/未识别/已停止/被更新代合并）。</summary>
    public bool TrySchedule(string payload, Func<CancellationToken, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (string.IsNullOrWhiteSpace(payload)) return false;

        var identity = _parse(payload);
        if (identity is null) return false;

        var command = new UiCommand(
            identity.Value.MergeKey,
            Interlocked.Increment(ref _generation),
            _source,
            _priorityOf(identity.Value.Kind),
            payload,
            execute);
        return _queue.Enqueue(command);
    }

    /// <summary>同步释放：只停止接收并请求取消，**不等待** worker（禁在 UI 线程阻塞）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.SignalStop();
    }

    /// <summary>异步释放：等待 worker 退出（有界：worker 只在当前命令完成/取消后结束）。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _queue.DisposeAsync().ConfigureAwait(false);
    }
}
