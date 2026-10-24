#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Strategy.Refresh.Core;

/// <summary>UI 命令优先级（交互 &gt; 刷新 &gt; 后台/流）。</summary>
public enum UiCommandPriority
{
    Stream = 0,
    Background = 1,
    Refresh = 2,
    Interactive = 3,
}

/// <summary>
/// UI 命令（L0 契约）：UI 线程只做 <see cref="UiCommandQueue.Enqueue"/>（O(1)），
/// 执行体在单消费者 worker 内串行运行；同 key 只保留最新一代（latest-wins）。
/// </summary>
/// <param name="Key">合并键（同键在飞/排队仅保留最新）。</param>
/// <param name="Generation">代际（递增；旧代结果丢弃）。</param>
/// <param name="Source">来源（webmessage/timer/event）。</param>
/// <param name="Priority">优先级（同键合并后按最新命令的优先级）。</param>
/// <param name="Payload">原始负载（审计用，可为 null）。</param>
/// <param name="Execute">worker 内执行体（禁触控件；DOM 落地经 IHtmlChannel 封送）。</param>
public sealed record UiCommand(
    string Key,
    long Generation,
    string Source,
    UiCommandPriority Priority,
    object? Payload,
    Func<CancellationToken, Task> Execute);

/// <summary>
/// UI 命令队列（F1）—— 线程边界单点：Enqueue 永不阻塞 UI；单 worker 串行执行；
/// 同键 latest-wins 合并（交互/刷新不会因排队放大）。零领域、零 Plate 依赖。
/// </summary>
public sealed class UiCommandQueue : IAsyncDisposable
{
    private readonly Channel<UiCommand> _channel;
    private readonly ConcurrentDictionary<string, UiCommand> _latest = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private long _generation;
    private long _enqueued;
    private long _executed;
    private long _coalesced;
    private long _failed;

    public UiCommandQueue()
    {
        _channel = Channel.CreateUnbounded<UiCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _worker = Task.Run(RunAsync);
    }

    /// <summary>已入队命令总数。</summary>
    public long Enqueued => Interlocked.Read(ref _enqueued);

    /// <summary>已执行命令总数（不含被合并丢弃的代）。</summary>
    public long Executed => Interlocked.Read(ref _executed);

    /// <summary>被同键新代合并/丢弃的命令数。</summary>
    public long Coalesced => Interlocked.Read(ref _coalesced);

    /// <summary>执行体抛异常次数（已诊断，不中断 worker）。</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>队列深度（待执行命令数，近似）。</summary>
    public int Pending => _latest.Count;

    /// <summary>分配单调代际（调用方也可自带）。</summary>
    public long NextGeneration() => Interlocked.Increment(ref _generation);

    /// <summary>入队（O(1)，UI 线程安全）：同键旧代在出队时被识别并丢弃。</summary>
    public bool Enqueue(UiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Key)) return false;
        _latest[command.Key] = command;
        Interlocked.Increment(ref _enqueued);
        return _channel.Writer.TryWrite(command);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var command in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                // latest-wins：同键已有更新命令 ⇒ 本命令过期，直接丢弃。
                if (!_latest.TryGetValue(command.Key, out var latest) || !ReferenceEquals(latest, command))
                {
                    Interlocked.Increment(ref _coalesced);
                    continue;
                }

                _latest.TryRemove(command.Key, out _);
                try
                {
                    await command.Execute(_cts.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref _executed);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // 关闭期取消：正常退出。
                }
                catch (Exception)
                {
                    // worker 不因单条命令失败而终止；诊断由执行体自身出口负责。
                    Interlocked.Increment(ref _failed);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _worker.ConfigureAwait(false); }
        catch { /* 关闭期异常不阻塞 */ }
        _cts.Dispose();
    }
}
