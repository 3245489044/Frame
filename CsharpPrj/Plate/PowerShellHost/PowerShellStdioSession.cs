using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>启动握手判定（由调用方给；通用会话不认识业务字段）。</summary>
public enum PowerShellHandshakeDecision
{
    /// <summary>非握手行（模块告警等），继续读下一行。</summary>
    Continue = 0,

    /// <summary>握手成功：本行即首行契约载荷，交由调用方解析。</summary>
    Ready = 1,

    /// <summary>握手失败：立即终止并可见化（不静默降级）。</summary>
    Failed = 2,
}

/// <summary>长驻会话选项（默认值 = 现有 PsRuntime 宿主的实际行为）。</summary>
public sealed record PowerShellSessionOptions
{
    public string WorkingDirectory { get; init; } = "";

    /// <summary>宿主脚本绝对路径（<c>-File &lt;hostScript&gt;</c>）。</summary>
    public string HostScript { get; init; } = "";

    /// <summary>追加在 <c>-File &lt;hostScript&gt;</c> 之后的参数。</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>环境白名单（null = 默认 keep 集，与历史 <c>ApplyEnvironmentSnapshot</c> 逐字一致）。</summary>
    public IReadOnlyList<string>? EnvironmentAllowList { get; init; }

    /// <summary>白名单之上的显式覆盖（如 <c>WF_PS_RUNTIME_POOL</c>）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public int StderrTailLines { get; init; } = 64;

    /// <summary>等待同 id 响应时允许跳过的非匹配行上限（与历史 32 一致）。</summary>
    public int MaxSkippedLines { get; init; } = 32;

    public bool SingleFlight { get; init; } = true;

    public bool AllowWindowsPowerShell51 { get; init; }

    /// <summary>显式配置的可执行路径（空 ⇒ 经 <see cref="PowerShellHostLocator"/> 定位）。</summary>
    public string? ConfiguredExecutable { get; init; }

    /// <summary>已物化的真实 pwsh 路径（框架解析结果；优先于 ConfiguredExecutable/Locate）。</summary>
    public string? ResolvedExecutable { get; init; }
}

/// <summary>一行响应（协议只解码 id/status/message；业务字段留给调用方）。</summary>
public sealed record PowerShellSessionResponse(
    string Id,
    string Status,
    string? Message,
    string RawJson,
    bool IsStartupPhaseFailure);

/// <summary>
/// PowerShell 长驻宿主会话（JSON Lines over stdio）：进程生命周期 / 编码 / 环境快照 / stderr 排空 /
/// 单飞请求 / 超时杀进程树 / 失败重启。
/// <para>
/// 职责边界：只保证「一行请求 → 一行同 id 响应」；<c>ready/contracts</c> 与
/// <c>action/params/data</c> 由调用方 handshake/mapper 处理。禁在本类引入业务字段。
/// </para>
/// </summary>
public sealed partial class PowerShellStdioSession : IAsyncDisposable
{
    private readonly PowerShellSessionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processSync = new();
    private readonly object _stderrSync = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private string? _executablePath;
    private string? _resolvedExecutableOverride;
    private long _sequence;
    private readonly Queue<string> _stderrTail = new();

    public PowerShellStdioSession(PowerShellSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>进程在运行（结构事实）。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_processSync)
            {
                try
                {
                    return _process is { HasExited: false } && _stdout is not null && _stdin is not null;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>解析到的可执行绝对路径（未启动 ⇒ null）。</summary>
    public string? ExecutablePath => _executablePath;

    /// <summary>注入框架解析结果（装配期一次解析；Restart 后仍复用）。</summary>
    public void UseResolvedExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return;
        _resolvedExecutableOverride = executablePath;
        _executablePath = executablePath;
    }

    /// <summary>stderr 尾部快照（有界；诊断面）。</summary>
    public IReadOnlyList<string> StderrTail
    {
        get
        {
            lock (_stderrSync) return _stderrTail.ToArray();
        }
    }

    /// <summary>下一请求 id（单调递增；跨重启保持唯一）。</summary>
    public string NextId() => Interlocked.Increment(ref _sequence).ToString();

    /// <summary>
    /// 启动会话并等待首行握手。返回被判定为 Ready 的首行原文（调用方自行解析业务字段）；
    /// 已运行则返回 null。失败抛 <see cref="PowerShellHostSessionException"/>（稳定码）。
    /// </summary>
    public async Task<string?> StartAsync(
        Func<string, PowerShellHandshakeDecision>? handshake = null,
        CancellationToken ct = default)
    {
        if (IsRunning) return null;

        Restart();

        var executable = _executablePath ??= !string.IsNullOrWhiteSpace(_resolvedExecutableOverride)
            ? _resolvedExecutableOverride!
            : !string.IsNullOrWhiteSpace(_options.ResolvedExecutable)
                ? _options.ResolvedExecutable!
                : PowerShellHostLocator.Locate(
                    _options.ConfiguredExecutable,
                    _options.AllowWindowsPowerShell51);

        var effectiveWorkingDirectory = !string.IsNullOrWhiteSpace(_options.WorkingDirectory)
                                        && Directory.Exists(_options.WorkingDirectory)
            ? _options.WorkingDirectory
            : (Directory.Exists(Environment.CurrentDirectory) ? Environment.CurrentDirectory : AppContext.BaseDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = effectiveWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        ApplyEnvironmentSnapshot(startInfo);

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_options.HostScript);
        if (_options.Arguments is not null)
        {
            foreach (var argument in _options.Arguments)
            {
                if (argument is null) continue;
                startInfo.ArgumentList.Add(argument);
            }
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw new PowerShellHostSessionException(
                          PowerShellHostCodes.SessionStartFailed,
                          $"pwsh 启动失败（Process.Start=null）：{executable}",
                          executable);
        }
        catch (PowerShellHostSessionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PowerShellHostSessionException(
                PowerShellHostCodes.SessionStartFailed,
                $"pwsh 启动失败（{ex.GetType().Name}）：{ex.Message}",
                executable,
                inner: ex);
        }

        lock (_processSync) { _process = process; }
        // 启动即持续排空 stderr；否则缓冲区写满会阻塞子进程（表现为随机超时）。
        _ = Task.Run(() => DrainStderrAsync(process.StandardError));

        var deadline = DateTimeOffset.UtcNow + _options.StartTimeout;
        string? failureLine = null;
        for (var attempt = 0; attempt < _options.MaxSkippedLines; attempt++)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            string? line;
            try
            {
                line = await ReadLineWithTimeoutAsync(process.StandardOutput, remaining, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                break;
            }

            if (line is null) break;

            var decision = handshake?.Invoke(line) ?? PowerShellHandshakeDecision.Ready;
            if (decision == PowerShellHandshakeDecision.Ready)
            {
                lock (_processSync)
                {
                    _stdin = process.StandardInput;
                    _stdout = process.StandardOutput;
                }
                return line;
            }

            if (decision == PowerShellHandshakeDecision.Failed)
            {
                failureLine = line;
                break;
            }
        }

        var tail = StderrTail;
        Restart();
        var detail = failureLine ?? "未收到启动自检 JSON";
        throw new PowerShellHostSessionException(
            PowerShellHostCodes.SessionStartFailed,
            $"PsRuntime 启动自检失败：{detail}",
            executable,
            tail);
    }

    private void ApplyEnvironmentSnapshot(ProcessStartInfo startInfo)
    {
        if (_options.EnvironmentAllowList is null)
        {
            PowerShellHostEnvironment.Apply(startInfo, _options.Environment);
            return;
        }

        // 显式白名单：保持调用方完全控制（仍经共享实现写入）。
        startInfo.Environment.Clear();
        foreach (var name in _options.EnvironmentAllowList)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) startInfo.Environment[name] = value;
        }

        if (_options.Environment is not null)
        {
            foreach (var pair in _options.Environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// 发送一行请求并等待同 id 响应；非匹配行按 <see cref="PowerShellSessionOptions.MaxSkippedLines"/> 跳过。
    /// 超时 → 杀进程树 + <see cref="Restart"/> + 稳定码失败（禁静默"无能力"）。
    /// </summary>
    public async Task<PowerShellSessionResponse> RequestAsync(
        string id,
        string requestJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("请求 id 不得为空。", nameof(id));
        if (string.IsNullOrWhiteSpace(requestJson))
            throw new ArgumentException("请求 JSON 不得为空。", nameof(requestJson));

        if (_options.SingleFlight) await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StreamWriter? stdin;
            StreamReader? stdout;
            lock (_processSync)
            {
                stdin = _stdin;
                stdout = _stdout;
            }

            if (stdin is null || stdout is null)
            {
                throw new PowerShellHostSessionException(
                    PowerShellHostCodes.SessionNotStarted,
                    "会话未启动（先调用 StartAsync）。",
                    _executablePath);
            }

            try
            {
                if (Environment.GetEnvironmentVariable("PS_SESSION_TRACE") == "1")
                    Console.WriteLine("PS_SESSION_REQ=" + requestJson);
                await stdin.WriteLineAsync(requestJson.AsMemory(), ct).ConfigureAwait(false);
                await stdin.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                var tail = StderrTail;
                Restart();
                throw new PowerShellHostSessionException(
                    PowerShellHostCodes.SessionExited,
                    "写入请求失败：宿主进程不可用。",
                    _executablePath,
                    tail,
                    ex);
            }

            for (var attempt = 0; attempt < _options.MaxSkippedLines; attempt++)
            {
                string? line;
                try
                {
                    line = await ReadLineWithTimeoutAsync(stdout, _options.RequestTimeout, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    var tail = StderrTail;
                    Restart();
                    throw new PowerShellHostSessionException(
                        PowerShellHostCodes.SessionTimeout,
                        $"PsRuntime 调用超时（{_options.RequestTimeout.TotalSeconds:0}s）：{id}",
                        _executablePath,
                        tail);
                }

                if (line is null)
                {
                    var tail = StderrTail;
                    Restart();
                    throw new PowerShellHostSessionException(
                        PowerShellHostCodes.SessionExited,
                        "PsRuntime 宿主进程已退出。",
                        _executablePath,
                        tail);
                }

                if (Environment.GetEnvironmentVariable("PS_SESSION_TRACE") == "1")
                    Console.WriteLine("PS_SESSION_LINE=" + (line.Length <= 200 ? line : line[..200]));

                if (!TryParseResponse(line, out var response)) continue;
                if (response.IsStartupPhaseFailure) return response;
                if (!string.Equals(response.Id, id, StringComparison.Ordinal)) continue;
                return response;
            }

            var unmatchedTail = StderrTail;
            Restart();
            throw new PowerShellHostSessionException(
                PowerShellHostCodes.SessionResponseUnmatched,
                "PsRuntime 未返回匹配响应。",
                _executablePath,
                unmatchedTail);
        }
        finally
        {
            if (_options.SingleFlight) _gate.Release();
        }
    }

    /// <summary>构造一行请求 JSON（纯函数；<c>params</c> 缺省为空对象）。</summary>
    public static string BuildRequestJson(string id, string action, JsonElement? parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("action", action);
            writer.WritePropertyName("params");
            if (parameters is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>杀进程树并释放流（失败路径不抛）。</summary>
    public void Restart()
    {
        lock (_processSync)
        {
            try
            {
                if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 重启路径不抛。
            }
            finally
            {
                try { _stdin?.Dispose(); } catch { }
                try { _stdout?.Dispose(); } catch { }
                try { _process?.Dispose(); } catch { }
                _stdin = null;
                _stdout = null;
                _process = null;
                if (string.IsNullOrWhiteSpace(_options.ResolvedExecutable)
                    && string.IsNullOrWhiteSpace(_resolvedExecutableOverride))
                    _executablePath = null; // 下次启动重新解析（Store 更新/路径漂移自愈）。
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Restart();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_stderrSync)
                {
                    _stderrTail.Enqueue(line);
                    while (_stderrTail.Count > _options.StderrTailLines) _stderrTail.Dequeue();
                }
            }
        }
        catch
        {
            // 进程退出 / 管道关闭：正常结束路径，不抛。
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        StreamReader reader,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero) cts.CancelAfter(timeout);
        try
        {
            return await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static bool TryParseResponse(string line, out PowerShellSessionResponse response)
    {
        response = new PowerShellSessionResponse("", "failure", "响应解析失败", line, false);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? "failure"
                : "failure";
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            {
                // 只接受启动相位（phase=startup）的无 id 失败行；运行期无 id 行不得归因到本次调用。
                var isStartup = root.TryGetProperty("phase", out var phase)
                                && phase.ValueKind == JsonValueKind.String
                                && string.Equals(phase.GetString(), "startup", StringComparison.OrdinalIgnoreCase);
                if (!isStartup || !string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase))
                    return false;

                response = new PowerShellSessionResponse(
                    "", status, message ?? "PsRuntime 返回无 id 的失败", line, true);
                return true;
            }

            response = new PowerShellSessionResponse(idElement.GetString() ?? "", status, message, line, false);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
