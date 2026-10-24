#nullable enable

using System;
using System.Text.Json;
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
public sealed class HostMessageCommandScheduler : IWebMessageScheduler, IDisposable
{
    private readonly UiCommandQueue _queue;
    private long _generation;

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

        var (key, priority) = Classify(payload);
        var command = new UiCommand(
            key,
            Interlocked.Increment(ref _generation),
            "webmessage",
            priority,
            payload,
            execute);
        return _queue.Enqueue(command);
    }

    private static (string Key, UiCommandPriority Priority) Classify(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var cmd = Field(root, "cmd") ?? Field(root, "event") ?? "message";
            var scope = Field(root, "session")
                        ?? Field(root, "history")
                        ?? Field(root, "task")
                        ?? Field(root, "name")
                        ?? string.Empty;
            var priority = cmd switch
            {
                "refresh" => UiCommandPriority.Refresh,
                "selectTask" or "selectHistory" or "selectType" => UiCommandPriority.Interactive,
                "tick" or "update" => UiCommandPriority.Background,
                _ => UiCommandPriority.Background,
            };
            return ($"{cmd}|{scope}", priority);
        }
        catch (JsonException)
        {
            return ("message", UiCommandPriority.Background);
        }
    }

    private static string? Field(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    public void Dispose() => _ = _queue.DisposeAsync().AsTask();
}
