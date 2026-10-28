using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>页面命令（data-payload 的解析结果）。未知字段忽略；参数值可能是 JsonElement，交给 ParamSet 归一。</summary>
public sealed class ConsoleCommand
{
    public string Cmd { get; set; } = "";

    public string? Capability { get; set; }

    public string? JobId { get; set; }

    public Dictionary<string, object?>? Params { get; set; }

    public string? Drawer { get; set; }

    public string? Filter { get; set; }

    public string? Path { get; set; }

    public string? Action { get; set; }

    public int Page { get; set; } = 1;
}

/// <summary>命令结果：需要写回的区域 HTML（region → 片段）与提示文本。</summary>
public sealed class ConsoleCommandOutcome
{
    public Dictionary<string, string> Regions { get; } = new(StringComparer.Ordinal);

    public string? Notice { get; set; }

    public bool Handled { get; set; }

    public static ConsoleCommandOutcome Unhandled(string notice) =>
        new() { Handled = false, Notice = notice };
}

/// <summary>控制台会话状态（选中能力/任务/抽屉/分页/表单值）；宿主负责持久化与恢复。</summary>
public sealed class ConsoleState
{
    public string? CapabilityId { get; set; }

    public string? JobId { get; set; }

    /// <summary>none / history / graph / diag。</summary>
    public string Drawer { get; set; } = "none";

    public string? Filter { get; set; }

    public int Page { get; set; } = 1;

    public Dictionary<string, object?> FormValues { get; } = new(StringComparer.Ordinal);

    public string? Notice { get; set; }
}

/// <summary>路由可选钩子：结构图、打开目录、诊断文本、状态栏。空实现 = 对应功能降级为提示。</summary>
public sealed class ConsoleRouterOptions
{
    /// <summary>能力 Id → 结构图 SVG（原文注入抽屉）。</summary>
    public Func<string, string?>? GraphProvider { get; set; }

    /// <summary>打开产物所在目录（OS 副作用留在插件侧）。返回 false 表示失败。</summary>
    public Func<string, Task<bool>>? OpenArtifact { get; set; }

    /// <summary>诊断文本（抽屉 diag）。</summary>
    public Func<Task<string?>>? Diagnose { get; set; }

    /// <summary>状态栏文本（如能力可用性/探活结论）。</summary>
    public Func<string?>? StatusProvider { get; set; }

    /// <summary>最近任务条数。</summary>
    public int RecentJobs { get; set; } = 30;
}

/// <summary>
/// 通用控制台命令路由：把页面命令翻译为「端口调用 + 区域 HTML」。
/// <para>内置命令见 <see cref="HandleAsync(ConsoleCommand, CancellationToken)"/>；插件专有命令经
/// <see cref="Register"/> 注入（如歌词即时校验 / 重新探活 / 清缓存）。</para>
/// </summary>
public sealed class ConsoleCommandRouter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IProductionCatalog _catalog;
    private readonly IProductionRunner _runner;
    private readonly IProductionHistory _history;
    private readonly IArtifactPublisher _publisher;
    private readonly ConsoleRouterOptions _options;
    private readonly Dictionary<string, Func<ConsoleCommand, CancellationToken, Task<ConsoleCommandOutcome>>> _custom =
        new(StringComparer.OrdinalIgnoreCase);

    // 产物物化缓存（终态任务不可变）：自动刷新 tick 不重复读盘/base64；上限 64 后整体清空
    private readonly Dictionary<string, IReadOnlyList<ConsoleArtifactView>> _artifactCache = new(StringComparer.Ordinal);
    private readonly object _cacheGate = new();

    public ConsoleCommandRouter(
        IProductionCatalog catalog,
        IProductionRunner runner,
        IProductionHistory history,
        IArtifactPublisher publisher,
        ConsoleState? state = null,
        ConsoleRouterOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        State = state ?? new ConsoleState();
        _options = options ?? new ConsoleRouterOptions();
    }

    public ConsoleState State { get; }

    /// <summary>注册插件专有命令（覆写同名内置命令）。</summary>
    public void Register(string cmd, Func<ConsoleCommand, CancellationToken, Task<ConsoleCommandOutcome>> handler)
    {
        if (string.IsNullOrWhiteSpace(cmd)) throw new ArgumentException("命令名为空", nameof(cmd));
        _custom[cmd.Trim()] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>构建整页模型（首挂载/整页刷新用）。</summary>
    public async Task<ConsolePageModel> BuildPageAsync(CancellationToken ct = default)
    {
        var capabilities = _catalog.List();
        EnsureCapability(capabilities);

        var jobs = _runner.Recent(Math.Max(1, _options.RecentJobs));
        var selected = ResolveSelectedJob(jobs);
        var artifacts = selected is null
            ? Array.Empty<ConsoleArtifactView>()
            : await MaterializeAsync(selected, ct).ConfigureAwait(false);

        PagedResult<JobSnapshot>? history = null;
        if (string.Equals(State.Drawer, "history", StringComparison.OrdinalIgnoreCase))
            history = _history.Query(new HistoryQuery
            {
                CapabilityId = null,
                Text = State.Filter,
                Page = State.Page,
                PageSize = 20,
            });

        var graph = string.Equals(State.Drawer, "graph", StringComparison.OrdinalIgnoreCase) && State.CapabilityId is not null
            ? _options.GraphProvider?.Invoke(State.CapabilityId)
            : null;

        string? diag = null;
        if (string.Equals(State.Drawer, "diag", StringComparison.OrdinalIgnoreCase) && _options.Diagnose is not null)
            diag = await _options.Diagnose().ConfigureAwait(false);

        return new ConsolePageModel
        {
            Capabilities = capabilities,
            SelectedCapabilityId = State.CapabilityId,
            FormValues = new Dictionary<string, object?>(State.FormValues, StringComparer.Ordinal),
            Jobs = jobs,
            SelectedJob = selected,
            Artifacts = artifacts,
            History = history,
            Drawer = State.Drawer,
            GraphSvg = graph,
            DrawerText = diag,
            StatusText = _options.StatusProvider?.Invoke(),
            Notice = State.Notice,
        };
    }

    /// <summary>处理页面负载（data-payload 原文；也接受 {payload:"..."} 包裹）。</summary>
    public async Task<ConsoleCommandOutcome> HandleAsync(string payloadJson, CancellationToken ct = default)
    {
        var json = Unwrap(payloadJson);
        ConsoleCommand? command;
        try
        {
            command = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ConsoleCommand>(json, Json);
        }
        catch (Exception ex)
        {
            return ConsoleCommandOutcome.Unhandled($"命令解析失败: {ex.Message}");
        }

        if (command is null || string.IsNullOrWhiteSpace(command.Cmd))
            return ConsoleCommandOutcome.Unhandled("命令为空");
        return await HandleAsync(command, ct).ConfigureAwait(false);
    }

    /// <summary>处理已解析命令（内置命令 + 插件注册命令）。</summary>
    public async Task<ConsoleCommandOutcome> HandleAsync(ConsoleCommand command, CancellationToken ct = default)
    {
        State.Notice = null;

        if (_custom.TryGetValue(command.Cmd, out var handler))
            return await handler(command, ct).ConfigureAwait(false);

        switch (command.Cmd.Trim().ToLowerInvariant())
        {
            case "cap.select":
            {
                var capabilities = _catalog.List();
                var cap = Find(capabilities, command.Capability);
                if (cap is null) return await NoticeAsync("能力不存在: " + command.Capability, ct).ConfigureAwait(false);

                State.CapabilityId = cap.Id;
                State.JobId = null;
                ResetForm(cap);
                return await RenderAsync(new[] { ConsoleRegions.Caps, ConsoleRegions.Form, ConsoleRegions.Result }, ct)
                    .ConfigureAwait(false);
            }

            case "form.reset":
            {
                var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                if (cap is null) return await NoticeAsync("未选择能力", ct).ConfigureAwait(false);
                State.CapabilityId = cap.Id;
                ResetForm(cap);
                return await RenderAsync(new[] { ConsoleRegions.Form }, ct).ConfigureAwait(false);
            }

            case "form.run":
            {
                var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                if (cap is null) return await NoticeAsync("未选择能力", ct).ConfigureAwait(false);

                State.CapabilityId = cap.Id;
                // 表单状态 → 命令显式参数覆盖（页面采集/程序化调用两种入口共用同一合并规则）
                var source = new Dictionary<string, object?>(State.FormValues, StringComparer.Ordinal);
                if (command.Params is not null)
                    foreach (var kv in command.Params)
                        source[kv.Key] = kv.Value;

                var merged = ParamSet.Merge(cap.Params, source);
                var errors = ParamSet.Validate(cap.Params, merged);
                if (errors.Count > 0)
                {
                    State.Notice = string.Join("；", errors);
                    return await RenderAsync(new[] { ConsoleRegions.Status, ConsoleRegions.Form }, ct).ConfigureAwait(false);
                }

                State.FormValues.Clear();
                foreach (var kv in merged) State.FormValues[kv.Key] = kv.Value;

                try
                {
                    var jobId = await _runner.SubmitAsync(cap.Id, merged, ct).ConfigureAwait(false);
                    State.JobId = jobId;
                }
                catch (Exception ex)
                {
                    State.Notice = $"提交失败: {ex.Message}";
                }

                return await RenderAsync(new[] { ConsoleRegions.Status, ConsoleRegions.Jobs, ConsoleRegions.Result }, ct)
                    .ConfigureAwait(false);
            }

            case "job.tick":
            {
                // 自动刷新：合并页面未提交的表单值（避免轮询覆盖编辑中的输入），不触发新任务
                if (command.Params is { Count: > 0 })
                {
                    var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                    if (cap is not null)
                        foreach (var kv in ParamSet.Merge(cap.Params, command.Params))
                            State.FormValues[kv.Key] = kv.Value;
                }
                return await RenderAsync(new[] { ConsoleRegions.Jobs, ConsoleRegions.Result, ConsoleRegions.Status }, ct)
                    .ConfigureAwait(false);
            }

            case "job.select":
            {
                State.JobId = command.JobId;
                return await RenderAsync(new[] { ConsoleRegions.Result }, ct).ConfigureAwait(false);
            }

            case "job.cancel":
            {
                if (string.IsNullOrWhiteSpace(command.JobId)) break;
                var ok = await _runner.CancelAsync(command.JobId!, ct).ConfigureAwait(false);
                if (!ok) State.Notice = "任务不可取消（可能已结束）";
                return await RenderAsync(new[] { ConsoleRegions.Status, ConsoleRegions.Jobs }, ct).ConfigureAwait(false);
            }

            case "job.reuse":
            {
                var job = _runner.Get(command.JobId ?? State.JobId ?? "");
                if (job is null) return await NoticeAsync("任务不存在", ct).ConfigureAwait(false);

                var cap = Find(_catalog.List(), job.CapabilityId);
                if (cap is null) return await NoticeAsync("能力不存在: " + job.CapabilityId, ct).ConfigureAwait(false);

                State.CapabilityId = cap.Id;
                State.JobId = job.JobId;
                State.FormValues.Clear();
                foreach (var kv in job.Params) State.FormValues[kv.Key] = kv.Value;
                return await RenderAsync(new[] { ConsoleRegions.Caps, ConsoleRegions.Form }, ct).ConfigureAwait(false);
            }

            case "job.retry":
            {
                var job = _runner.Get(command.JobId ?? State.JobId ?? "");
                if (job is null) return await NoticeAsync("任务不存在", ct).ConfigureAwait(false);
                if (!job.IsTerminal) return await NoticeAsync("任务尚未结束，不能重试", ct).ConfigureAwait(false);

                try
                {
                    var newId = await _runner.SubmitAsync(job.CapabilityId, job.Params, ct).ConfigureAwait(false);
                    State.JobId = newId;
                }
                catch (Exception ex)
                {
                    State.Notice = $"重试提交失败: {ex.Message}";
                }
                return await RenderAsync(new[] { ConsoleRegions.Status, ConsoleRegions.Jobs, ConsoleRegions.Result }, ct)
                    .ConfigureAwait(false);
            }

            case "drawer.open":
            {
                State.Drawer = NormalizeDrawer(command.Drawer);
                State.Page = 1;
                return await RenderAsync(new[] { ConsoleRegions.Drawer }, ct).ConfigureAwait(false);
            }

            case "history.query":
            {
                State.Drawer = "history";
                State.Filter = command.Filter;
                State.Page = Math.Max(1, command.Page);
                return await RenderAsync(new[] { ConsoleRegions.Drawer }, ct).ConfigureAwait(false);
            }

            case "artifact.open":
            {
                if (string.IsNullOrWhiteSpace(command.Path))
                    return await NoticeAsync("产物路径为空", ct).ConfigureAwait(false);
                if (_options.OpenArtifact is null)
                    return await NoticeAsync("未接入「打开目录」能力", ct).ConfigureAwait(false);

                var opened = await _options.OpenArtifact(command.Path!).ConfigureAwait(false);
                if (!opened) State.Notice = "打开目录失败: " + command.Path;
                return await RenderAsync(new[] { ConsoleRegions.Status }, ct).ConfigureAwait(false);
            }

            case "cap.action":
            {
                return ConsoleCommandOutcome.Unhandled("动作未实现: " + command.Action);
            }
        }

        return ConsoleCommandOutcome.Unhandled("未知命令: " + command.Cmd);
    }

    private async Task<ConsoleCommandOutcome> NoticeAsync(string notice, CancellationToken ct)
    {
        State.Notice = notice;
        return await RenderAsync(new[] { ConsoleRegions.Status }, ct).ConfigureAwait(false);
    }

    private async Task<ConsoleCommandOutcome> RenderAsync(string[] regions, CancellationToken ct)
    {
        var model = await BuildPageAsync(ct).ConfigureAwait(false);
        var outcome = new ConsoleCommandOutcome { Handled = true, Notice = State.Notice };
        foreach (var region in regions)
            outcome.Regions[region] = ConsoleHtmlKit.RenderRegion(region, model);
        return outcome;
    }

    private void EnsureCapability(IReadOnlyList<CapabilityDescriptor> capabilities)
    {
        if (capabilities.Count == 0)
        {
            State.CapabilityId = null;
            State.FormValues.Clear();
            return;
        }

        var current = Find(capabilities, State.CapabilityId);
        if (current is null)
        {
            current = capabilities[0];
            State.CapabilityId = current.Id;
            ResetForm(current);
        }
    }

    private void ResetForm(CapabilityDescriptor capability)
    {
        State.FormValues.Clear();
        foreach (var kv in ParamSet.Defaults(capability.Params))
            State.FormValues[kv.Key] = kv.Value;
    }

    private JobSnapshot? ResolveSelectedJob(IReadOnlyList<JobSnapshot> jobs)
    {
        if (!string.IsNullOrWhiteSpace(State.JobId))
        {
            var selected = _runner.Get(State.JobId!);
            if (selected is not null) return selected;
        }
        return jobs.FirstOrDefault();
    }

    private async Task<IReadOnlyList<ConsoleArtifactView>> MaterializeAsync(JobSnapshot job, CancellationToken ct)
    {
        if (job.Artifacts.Count == 0) return Array.Empty<ConsoleArtifactView>();

        var cacheKey = $"{job.JobId}|{job.Status}|{job.Artifacts.Count}";
        if (job.IsTerminal)
        {
            lock (_cacheGate)
                if (_artifactCache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var views = new List<ConsoleArtifactView>(job.Artifacts.Count);
        foreach (var artifact in job.Artifacts)
        {
            var url = await _publisher.MaterializeAsync(artifact, ct).ConfigureAwait(false);
            string? text = null;
            if (artifact.Kind is ArtifactKind.Text or ArtifactKind.Json
                && artifact.Bytes <= 64 * 1024
                && File.Exists(artifact.Path))
            {
                try { text = await File.ReadAllTextAsync(artifact.Path, ct).ConfigureAwait(false); }
                catch { /* 读取失败保留路径 */ }
            }
            views.Add(new ConsoleArtifactView { Spec = artifact, Url = url, Text = text });
        }

        if (job.IsTerminal)
        {
            lock (_cacheGate)
            {
                if (_artifactCache.Count >= 64) _artifactCache.Clear();
                _artifactCache[cacheKey] = views;
            }
        }
        return views;
    }

    private static CapabilityDescriptor? Find(IReadOnlyList<CapabilityDescriptor> capabilities, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : capabilities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDrawer(string? drawer) => (drawer ?? "none").Trim().ToLowerInvariant() switch
    {
        "history" => "history",
        "graph" => "graph",
        "diag" => "diag",
        _ => "none",
    };

    private static string Unwrap(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.String)
                return payload.GetString() ?? "";
        }
        catch
        {
            // 非 JSON 原文 → 交给下方反序列化报错
        }
        return payloadJson;
    }
}
