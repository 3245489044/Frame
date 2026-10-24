#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Strategy.Refresh.Core;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 页面消息 → 通用命令队列适配器（F1）：UI 线程只做分类 + 入队（O(1)），
/// 同键（cmd + 作用域）latest-wins 合并，worker 串行执行。
/// <para>循环探针等必须同步回投的消息不改道（返回 false ⇒ 运行时就地执行）。</para>
/// </summary>
public sealed class HostMessageCommandScheduler : IWebMessageScheduler, IDisposable, IAsyncDisposable
{
    private readonly UiCommandQueue _queue;
    private long _generation;
    private int _disposed;

    public HostMessageCommandScheduler(UiCommandQueue? queue = null)
        => _queue = queue ?? new UiCommandQueue();

    /// <summary>底层队列（指标/诊断只读）。</summary>
    public UiCommandQueue Queue => _queue;

    public bool TrySchedule(string payload, Func<CancellationToken, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (string.IsNullOrWhiteSpace(payload)) return false;
        // 回流自检必须走原路（探针等待即时回投；排队会引入假超时）。
        if (payload.IndexOf("__loopback__", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // 唯一解析（UiMessageEnvelope）：合并键/优先级同源，本层不再二次 Parse（DUP-06）。
        var envelope = UiMessageEnvelope.Parse(payload);
        var command = new UiCommand(
            envelope.MergeKey,
            Interlocked.Increment(ref _generation),
            "webmessage",
            PriorityOf(envelope.Kind),
            payload,
            execute);
        return _queue.Enqueue(command);
    }

    /// <summary>命令 → 优先级（显式表；表外命令按 <see cref="UiCommandPriority.Interactive"/>：未知命令不得被后台合并/延迟）。</summary>
    private static UiCommandPriority PriorityOf(string kind) => kind.ToLowerInvariant() switch
    {
        "refresh" => UiCommandPriority.Refresh,
        "selecttask" or "selecthistory" or "selecttype" or "selectnode" => UiCommandPriority.Interactive,
        "tick" or "update" or "message" or "stream" => UiCommandPriority.Background,
        _ => UiCommandPriority.Interactive,
    };

    /// <summary>同步释放：只停止接收并请求取消，**不等待** worker（禁在 UI 线程阻塞）；异步宿主用 <see cref="DisposeAsync"/>。</summary>
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
