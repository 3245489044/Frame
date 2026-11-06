using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Publish;

/// <summary>
/// 实时事件出口（E5 投影，<b>零落盘</b>）：命名管道服务端，向每个已连接客户端流式写 NDJSON
/// （一行一条 <see cref="PersistedEvent"/>，与文件事件区行 schema 一致），并接受控制帧做「按 scope 回溯 / 过滤 / 保活」。
/// <para>
/// 纪律：<see cref="Push"/> 只做「投递到各客户端有界队列」——<b>绝不等待 IO</b>（<c>ObservabilityBus.Emit</c> 在锁内
/// 同步串行调用 <c>Sink.Push</c>，关键路径禁阻塞）。慢客户端由有界队列丢弃最旧并计入可见账 <see cref="DroppedCount"/>。
/// </para>
/// <para>
/// 管道名 = <c>{prefix}-{pid}[-n]</c>（多实例并存自动加后缀）；PS 侧按前缀枚举 <c>\\.\pipe\{prefix}*</c> 发现，
/// <b>无需落盘名册文件</b>。消费方 = <c>Frame\PowerShellScript\Share\Observe\Watch-AgentObserve.ps1</c>。
/// </para>
/// </summary>
public sealed class LiveEventPipe : IAsyncDisposable
{
    /// <summary>缺省管道名前缀（PS 侧按此前缀枚举发现）。</summary>
    public const string DefaultPrefix = "agent-observe";

    private const int DefaultQueueCapacity = 4096;

    private readonly string _name;
    private readonly int _queueCapacity;
    private readonly IEventStore? _store;
    private readonly List<ClientSession> _clients = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private long _pushedTotal;

    private LiveEventPipe(string name, int queueCapacity, IEventStore? store)
    {
        _name = name;
        _queueCapacity = queueCapacity;
        _store = store;
    }

    /// <summary>实际管道名（PS 连接用）。</summary>
    public string Name => _name;

    /// <summary>当前已连接客户端数。</summary>
    public int ClientsConnected
    {
        get { lock (_gate) return _clients.Count; }
    }

    /// <summary>累计投递条数（只增）。</summary>
    public long PushedTotal => Interlocked.Read(ref _pushedTotal);

    /// <summary>各客户端因队列满被丢弃的累计条数（可见账）。</summary>
    public long DroppedCount
    {
        get { lock (_gate) return _clients.Sum(c => c.DroppedCount); }
    }

    /// <summary>
    /// 启动管道服务。<paramref name="store"/> = 事实源（供控制帧按 scope 回溯；null = 仅实时流）；
    /// <paramref name="queueCapacity"/> = 每客户端有界队列容量（满则丢最旧）。
    /// </summary>
    public static LiveEventPipe Start(string prefix = DefaultPrefix, IEventStore? store = null, int queueCapacity = DefaultQueueCapacity)
    {
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentNullException(nameof(prefix));
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), queueCapacity, "队列容量须为正数");

        var pipe = new LiveEventPipe(ResolveName(prefix), queueCapacity, store);
        pipe._cts = new CancellationTokenSource();
        _ = Task.Run(() => pipe.AcceptLoopAsync(pipe._cts.Token));
        return pipe;
    }

    /// <summary>实时投递（非阻塞）：入各客户端有界队列，满则丢最旧（计入客户端可见账）。</summary>
    public void Push(PersistedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        Interlocked.Increment(ref _pushedTotal);

        ClientSession[] sessions;
        lock (_gate) sessions = _clients.ToArray();
        foreach (var s in sessions) s.Enqueue(evt);
    }

    /// <summary>管道名解析：<c>{prefix}-{pid}</c>；被占用则追加 <c>-n</c>（同进程多实例可并存）。</summary>
    private static string ResolveName(string prefix)
    {
        var pid = Environment.ProcessId;
        for (var i = 0; i < 64; i++)
        {
            var candidate = i == 0 ? $"{prefix}-{pid}" : $"{prefix}-{pid}-{i + 1}";
            if (IsNameFree(candidate)) return candidate;
        }
        return $"{prefix}-{pid}-{Guid.NewGuid():N}";
    }

    private static bool IsNameFree(string name)
    {
        try
        {
            using var probe = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { server?.Dispose(); return; }
            catch (ObjectDisposedException) { server?.Dispose(); return; }
            catch (IOException)
            {
                server?.Dispose();
                await Task.Delay(50, ct).ConfigureAwait(false);
                continue;
            }

            var session = new ClientSession(server, _queueCapacity, _store, this);
            lock (_gate) _clients.Add(session);
            _ = session.RunAsync().ContinueWith(
                _ => { lock (_gate) _clients.Remove(session); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { /* 取消失败不阻断释放 */ }
            cts.Dispose();
        }

        ClientSession[] sessions;
        lock (_gate)
        {
            sessions = _clients.ToArray();
            _clients.Clear();
        }
        foreach (var s in sessions) s.Close();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 单客户端会话：有界出队通道（丢最旧）+ 写循环（NDJSON）+ 读循环（控制帧）。
    /// 过滤条件由控制帧设置（scope/kind），仅影响该客户端的实时流，不影响事实源。
    /// </summary>
    private sealed class ClientSession
    {
        private readonly NamedPipeServerStream _stream;
        private readonly Channel<PersistedEvent> _outbound;
        private readonly IEventStore? _store;
        private readonly LiveEventPipe _owner;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private long _enqueued;
        private long _written;
        private string? _scopeFilter;
        private string? _kindFilter;
        private volatile bool _closed;

        public ClientSession(NamedPipeServerStream stream, int capacity, IEventStore? store, LiveEventPipe owner)
        {
            _stream = stream;
            _store = store;
            _owner = owner;
            _outbound = Channel.CreateBounded<PersistedEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        /// <summary>被有界队列丢弃的条数（入队 - 已写 - 在队）。</summary>
        public long DroppedCount
            => Math.Max(0, Interlocked.Read(ref _enqueued) - Interlocked.Read(ref _written) - _outbound.Reader.Count);

        public void Enqueue(PersistedEvent evt)
        {
            if (_closed || !Matches(evt)) return;
            Interlocked.Increment(ref _enqueued);
            _outbound.Writer.TryWrite(evt);
        }

        public void Close()
        {
            _closed = true;
            _outbound.Writer.TryComplete();
            try { _stream.Dispose(); } catch { /* 已断开 */ }
        }

        public async Task RunAsync()
        {
            var reader = new StreamReader(_stream, new UTF8Encoding(false));
            var writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = false };
            try
            {
                await SendAsync(writer, new
                {
                    t = "hello",
                    v = 1,
                    pid = Environment.ProcessId,
                    pipe = _owner._name,
                    ring = (_store as RingEventStore)?.Capacity ?? 0,
                    clients = _owner.ClientsConnected,
                }).ConfigureAwait(false);

                var write = WriteLoopAsync(writer);
                var read = ReadLoopAsync(reader, writer);
                await Task.WhenAny(write, read).ConfigureAwait(false);
            }
            catch (IOException) { /* 客户端断开 */ }
            catch (OperationCanceledException) { /* 停机 */ }
            catch (ObjectDisposedException) { /* 停机 */ }
            finally
            {
                _closed = true;
                _outbound.Writer.TryComplete();
                try { _stream.Dispose(); } catch { /* 已释放 */ }
            }
        }

        private bool Matches(PersistedEvent evt)
        {
            if (_scopeFilter is not null && !string.Equals(evt.ScopeId, _scopeFilter, StringComparison.Ordinal)) return false;
            if (_kindFilter is not null
                && !evt.Event.EventKind.Contains(_kindFilter, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private async Task SendAsync(StreamWriter writer, object frame)
        {
            var json = JsonSerializer.Serialize(frame, ObservabilityWire.Json);
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        private async Task WriteLoopAsync(StreamWriter writer)
        {
            while (await _outbound.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_outbound.Reader.TryRead(out var evt))
                {
                    await SendAsync(writer, new { t = "event", e = evt }).ConfigureAwait(false);
                    Interlocked.Increment(ref _written);
                }
            }
        }

        private async Task ReadLoopAsync(StreamReader reader, StreamWriter writer)
        {
            while (true)
            {
                string? line;
                try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
                catch (IOException) { return; }
                if (line is null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonElement root;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    root = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    await SendAsync(writer, new { t = "error", message = "非法控制帧（非 JSON）" }).ConfigureAwait(false);
                    continue;
                }

                var kind = root.TryGetProperty("t", out var tp) ? tp.GetString() : null;
                switch (kind)
                {
                    case "ping":
                        await SendAsync(writer, new { t = "pong" }).ConfigureAwait(false);
                        break;
                    case "tail":
                        await HandleTailAsync(writer, root).ConfigureAwait(false);
                        break;
                    case "query":
                        await HandleQueryAsync(writer, root).ConfigureAwait(false);
                        break;
                    case "quit":
                        return;
                    default:
                        await SendAsync(writer, new { t = "error", message = $"未知帧类型：{kind}" }).ConfigureAwait(false);
                        break;
                }
            }
        }

        private async Task HandleTailAsync(StreamWriter writer, JsonElement root)
        {
            _scopeFilter = ReadString(root, "scope");
            _kindFilter = ReadString(root, "kind");

            var backlog = ReadInt(root, "backlog", 0);
            if (backlog <= 0) return;

            if (_store is not IRecentEvents recent)
            {
                await SendAsync(writer, new { t = "error", message = "事件源不支持回放（未装配有界内存环）" }).ConfigureAwait(false);
                return;
            }

            var raw = recent.RecentSnapshot(backlog);
            var events = raw.Where(Matches).ToList();
            foreach (var e in events)
            {
                await SendAsync(writer, new { t = "event", e, backlog = true }).ConfigureAwait(false);
                Interlocked.Increment(ref _written);
            }

            var ring = _store as RingEventStore;
            var droppedTotal = ring?.DroppedCount ?? 0;
            await SendAsync(writer, new
            {
                t = "backlogEnd",
                count = events.Count,
                ring = ring?.Capacity ?? 0,
                droppedTotal,                                    // 生命周期累计丢弃（非本次窗口）
                truncated = droppedTotal > 0 && raw.Count < backlog,  // 本次回放窗口被环边界截断
                corrupt = (_store as IEventStoreDiagnostics)?.CorruptLineCount ?? 0,
            }).ConfigureAwait(false);
        }

        private async Task HandleQueryAsync(StreamWriter writer, JsonElement root)
        {
            var id = ReadInt(root, "id", 0);
            var scope = ReadString(root, "scope");
            if (string.IsNullOrEmpty(scope))
            {
                await SendAsync(writer, new { t = "error", id, message = "query 帧须给 scope" }).ConfigureAwait(false);
                return;
            }
            if (_store is null)
            {
                await SendAsync(writer, new { t = "error", id, message = "未装配事件源" }).ConfigureAwait(false);
                return;
            }

            var afterSeq = ReadLong(root, "afterSeq", 0);
            var limit = ReadInt(root, "limit", 500);
            var effectiveLimit = limit <= 0 ? 500 : limit;
            var kinds = ReadStrings(root, "kinds");

            // limit 必须在 kind 过滤之后生效：先取全窗口（store 侧只做 scope+afterSeq+排序），
            // 再按 kind 过滤，最后截到 limit —— 否则「前 limit 条恰好都不是目标 kind」会误报空窗。
            var items = new List<PersistedEvent>();
            await foreach (var e in _store.TimelineAsync(
                scope, afterSeq > 0 ? afterSeq : null, int.MaxValue).ConfigureAwait(false))
            {
                if (kinds is { Count: > 0 } && !kinds.Contains(e.Event.EventKind, StringComparer.Ordinal)) continue;
                if (items.Count >= effectiveLimit) break;
                items.Add(e);
            }

            var nextAfterSeq = items.Count > 0
                ? items[^1].EventSeq
                : afterSeq > 0 ? afterSeq : (long?)null;

            await SendAsync(writer, new { t = "items", id, events = items }).ConfigureAwait(false);

            var ring = _store as RingEventStore;
            var droppedTotal = ring?.DroppedCount ?? 0;
            await SendAsync(writer, new
            {
                t = "end",
                id,
                scope,
                matched = items.Count,
                nextAfterSeq,
                ring = ring?.Capacity ?? 0,
                droppedTotal,                                      // 生命周期累计丢弃（非本次窗口）
                truncated = ring is not null && EventWindow.WindowTruncated(
                    afterSeq > 0 ? afterSeq : null, droppedTotal),  // 本次窗口被环边界截断
                corrupt = (_store as IEventStoreDiagnostics)?.CorruptLineCount ?? 0,
            }).ConfigureAwait(false);
        }

        private static string? ReadString(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int ReadInt(JsonElement root, string name, int fallback)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
                ? n : fallback;

        private static long ReadLong(JsonElement root, string name, long fallback)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
                ? n : fallback;

        private static List<string>? ReadStrings(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
            return list;
        }
    }
}
