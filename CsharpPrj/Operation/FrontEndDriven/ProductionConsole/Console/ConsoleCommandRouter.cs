using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrontEndDriven.ProductionConsole.Assistant;
using FrontEndDriven.ProductionConsole.Runtime;

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

    /// <summary>本拍构建的页面模型（宿主可复用于整页文本投影，免重复取数）。</summary>
    public ConsolePageModel? Model { get; set; }

    public static ConsoleCommandOutcome Unhandled(string notice) =>
        new() { Handled = false, Notice = notice };
}

/// <summary>插件侧操作结果（删除/校验）：Success 决定提示样式，Message 为可诊断文本。</summary>
public sealed class ConsoleOperationResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public static ConsoleOperationResult Ok(string message) => new() { Success = true, Message = message };

    public static ConsoleOperationResult Failed(string message) => new() { Success = false, Message = message };
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

    /// <summary>JSON 参数提案端口（自然语言 → 提案 JSON）；null = 该插件未接入提案能力（助理区给出接入提示）。</summary>
    public IJsonProposalSource? ProposalSource { get; set; }

    /// <summary>删除终态任务与产物（含历史行）；null = 未接入删除能力。</summary>
    public Func<string, CancellationToken, Task<ConsoleOperationResult>>? DeleteJob { get; set; }

    /// <summary>校验任务产物（存在/签名/尺寸/哈希）；null = 未接入校验能力。</summary>
    public Func<string, CancellationToken, Task<ConsoleOperationResult>>? VerifyArtifact { get; set; }

    /// <summary>最近任务条数。</summary>
    public int RecentJobs { get; set; } = 30;

    /// <summary>抽屉声明（渲染 tab 与内容分支的唯一来源；宿主可整体替换）。</summary>
    public IReadOnlyList<ConsoleDrawerSpec> Drawers { get; set; } = DefaultDrawers;

    /// <summary>插件命令别名 → 框架通用命令（框架内置通用名，领域命令名由别名表接入）。</summary>
    public IReadOnlyDictionary<string, string> CommandAliases { get; set; } = DefaultCommandAliases;

    /// <summary>默认抽屉声明（容器分页 / 结构图 / 诊断文本）。</summary>
    public static readonly IReadOnlyList<ConsoleDrawerSpec> DefaultDrawers = new[]
    {
        new ConsoleDrawerSpec("history", "历史", DrawerKind.Container),
        new ConsoleDrawerSpec("graph", "结构图", DrawerKind.Graph),
        new ConsoleDrawerSpec("diag", "诊断", DrawerKind.Text),
    };

    /// <summary>默认命令别名（历史查询 → 通用容器查询）。</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultCommandAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["history.query"] = "container.query",
        };
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
    private readonly IProductionContainer _history;
    private readonly ConsoleRouterOptions _options;
    private readonly ParameterAssistant _assistant;
    private readonly Func<ConsoleTemplateSet> _templates;
    private readonly ConsoleArtifactCache _artifacts;
    private readonly Dictionary<string, Func<ConsoleCommand, CancellationToken, Task<ConsoleCommandOutcome>>> _custom =
        new(StringComparer.OrdinalIgnoreCase);

    public ConsoleCommandRouter(
        IProductionCatalog catalog,
        IProductionRunner runner,
        IProductionContainer history,
        IArtifactPublisher publisher,
        ConsoleState? state = null,
        ConsoleRouterOptions? options = null,
        Func<ConsoleTemplateSet>? templates = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        if (publisher is null) throw new ArgumentNullException(nameof(publisher));
        State = state ?? new ConsoleState();
        _options = options ?? new ConsoleRouterOptions();
        // 模板惰性解析（宿主装配/面板路径就绪后可自愈），不在构造期固化。
        _templates = templates ?? (static () => ConsoleTemplateSet.Empty);
        _assistant = new ParameterAssistant(new ParameterAssistantState(), _options.ProposalSource);
        _artifacts = new ConsoleArtifactCache(publisher);
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
            : await _artifacts.MaterializeAsync(selected, ct).ConfigureAwait(false);

        PagedResult<JobSnapshot>? history = null;
        var drawerSpec = FindDrawer(State.Drawer);
        if (drawerSpec?.Kind == DrawerKind.Container)
            history = _history.Query(new ContainerQuery
            {
                CapabilityId = null,
                Text = State.Filter,
                Page = State.Page,
                PageSize = 20,
            });

        var graph = drawerSpec?.Kind == DrawerKind.Graph && State.CapabilityId is not null
            ? _options.GraphProvider?.Invoke(State.CapabilityId)
            : null;

        string? diag = null;
        if (drawerSpec?.Kind == DrawerKind.Text && _options.Diagnose is not null)
            diag = await _options.Diagnose().ConfigureAwait(false);

        return new ConsolePageModel
        {
            Drawers = _options.Drawers,
            Capabilities = capabilities,
            SelectedCapabilityId = State.CapabilityId,
            FormValues = new Dictionary<string, object?>(State.FormValues, StringComparer.Ordinal),
            Jobs = jobs,
            SelectedJob = selected,
            Artifacts = artifacts,
            History = history,
            HistoryFilter = State.Filter,
            Drawer = State.Drawer,
            GraphSvg = graph,
            DrawerText = diag,
            StatusText = _options.StatusProvider?.Invoke(),
            Notice = State.Notice,
            Assistant = _assistant.Snapshot(State.CapabilityId),
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

        var cmd = ResolveCommandName(command.Cmd);
        if (!string.Equals(cmd, command.Cmd, StringComparison.OrdinalIgnoreCase)
            && _custom.TryGetValue(cmd, out var aliasHandler))
            return await aliasHandler(command, ct).ConfigureAwait(false);

        switch (cmd.ToLowerInvariant())
        {
            case "cap.select":
            {
                var capabilities = _catalog.List();
                var cap = Find(capabilities, command.Capability);
                if (cap is null) return await NoticeAsync("能力不存在: " + command.Capability, ct).ConfigureAwait(false);

                State.CapabilityId = cap.Id;
                State.JobId = null;
                ResetForm(cap);
                return await RenderAsync(
                    new[] { ConsoleRegions.Caps, ConsoleRegions.Form, ConsoleRegions.Assist, ConsoleRegions.Result }, ct)
                    .ConfigureAwait(false);
            }

            case "form.reset":
            {
                var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                if (cap is null) return await NoticeAsync("未选择能力", ct).ConfigureAwait(false);
                State.CapabilityId = cap.Id;
                ResetForm(cap);
                return await RenderAsync(new[] { ConsoleRegions.Form, ConsoleRegions.Assist }, ct).ConfigureAwait(false);
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

                return await RenderAsync(
                    new[] { ConsoleRegions.Status, ConsoleRegions.Jobs, ConsoleRegions.Result, ConsoleRegions.Assist }, ct)
                    .ConfigureAwait(false);
            }

            case "assist.propose":
            {
                var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                if (cap is null) return await NoticeAsync("未选择能力", ct).ConfigureAwait(false);

                // 解析只刷新助理区：不重排表单（焦点/滚动/未提交编辑均不受影响）
                var text = command.Params is not null && command.Params.TryGetValue("__nl", out var raw)
                    ? raw?.ToString()
                    : null;
                await _assistant.ProposeAsync(cap, text, ct).ConfigureAwait(false);
                return await RenderAsync(new[] { ConsoleRegions.Assist, ConsoleRegions.Status }, ct).ConfigureAwait(false);
            }

            case "assist.apply":
            {
                var cap = Find(_catalog.List(), command.Capability ?? State.CapabilityId);
                if (cap is null) return await NoticeAsync("未选择能力", ct).ConfigureAwait(false);

                // 先吸收页面未提交的表单值：apply 只覆盖被建议键，其余保持用户当前输入
                AbsorbFormValues(cap, command.Params);

                var applied = _assistant.ApplyTo(cap.Id, State.FormValues);
                State.Notice = applied > 0
                    ? $"已应用 {applied} 项提案参数"
                    : "没有待应用的提案，请先点「AI 解析」";
                return await RenderAsync(
                    new[] { ConsoleRegions.Assist, ConsoleRegions.Form, ConsoleRegions.Status }, ct)
                    .ConfigureAwait(false);
            }

            case "job.tick":
            case "refresh":
            {
                // 自动刷新（标准协议命令名 refresh；job.tick 保留为脚本兼容别名）：
                // 合并页面未提交的表单值（避免刷新覆盖编辑中的输入），不触发新任务。
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
                return await RenderAsync(
                    new[] { ConsoleRegions.Caps, ConsoleRegions.Form, ConsoleRegions.Assist }, ct).ConfigureAwait(false);
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

            case "container.query":
            {
                State.Drawer = _options.Drawers.FirstOrDefault(d => d.Kind == DrawerKind.Container)?.Key ?? "none";
                // 筛选值既可走显式 Filter 字段，也可由页面 collect() 以 params.filter 回传。
                var filter = command.Filter;
                if (filter is null && command.Params is not null
                    && command.Params.TryGetValue("filter", out var filterRaw))
                    filter = filterRaw?.ToString();
                State.Filter = filter;
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

            case "artifact.verify":
            {
                var jobId = command.JobId ?? State.JobId;
                if (string.IsNullOrWhiteSpace(jobId))
                    return await NoticeAsync("未选择任务", ct).ConfigureAwait(false);
                if (_options.VerifyArtifact is null)
                    return await NoticeAsync("未接入「产物校验」能力", ct).ConfigureAwait(false);

                var result = await _options.VerifyArtifact(jobId!, ct).ConfigureAwait(false);
                State.Notice = result.Message;
                return await RenderAsync(new[] { ConsoleRegions.Status, ConsoleRegions.Result }, ct).ConfigureAwait(false);
            }

            case "job.delete":
            {
                var jobId = command.JobId ?? State.JobId;
                if (string.IsNullOrWhiteSpace(jobId))
                    return await NoticeAsync("未选择任务", ct).ConfigureAwait(false);
                if (_options.DeleteJob is null)
                    return await NoticeAsync("未接入「删除任务」能力", ct).ConfigureAwait(false);

                var result = await _options.DeleteJob(jobId!, ct).ConfigureAwait(false);
                State.Notice = result.Message;
                if (result.Success && string.Equals(State.JobId, jobId, StringComparison.Ordinal))
                    State.JobId = null;
                return await RenderAsync(
                    new[] { ConsoleRegions.Status, ConsoleRegions.Jobs, ConsoleRegions.Result, ConsoleRegions.Drawer }, ct)
                    .ConfigureAwait(false);
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
        var templates = _templates();
        var outcome = new ConsoleCommandOutcome { Handled = true, Model = model };
        foreach (var region in regions)
        {
            // 未声明区域不得作为写回目标（否则运行时 NOT_FOUND 静默丢更新；声明真源 = 面板 htmlPage.regions）。
            if (!templates.IsDeclaredRegion(region))
            {
                State.Notice = $"写回区域未声明: {region}（面板 htmlPage.regions）";
                continue;
            }
            outcome.Regions[region] = ConsoleHtmlKit.RenderRegion(region, model, templates);
        }
        outcome.Notice = State.Notice;
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
        // 切换/重置能力时清空提案态，避免把上一能力的提案应用到新能力
        _assistant.Invalidate(capability.Id);
    }

    /// <summary>把页面回传的已声明键写入表单态（__nl 等未声明键在此丢弃，不进入任务参数）。</summary>
    private void AbsorbFormValues(CapabilityDescriptor capability, Dictionary<string, object?>? values)
    {
        if (values is null) return;
        foreach (var spec in capability.Params)
            if (values.TryGetValue(spec.Key, out var value) && value is not null)
                State.FormValues[spec.Key] = ParamSet.Coerce(spec.Kind, value);
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

    private static CapabilityDescriptor? Find(IReadOnlyList<CapabilityDescriptor> capabilities, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : capabilities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>命令名归一：别名 → 框架通用命令（领域命令名不进入框架 switch）。</summary>
    private string ResolveCommandName(string cmd)
    {
        var key = (cmd ?? string.Empty).Trim();
        return _options.CommandAliases.TryGetValue(key, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
            ? mapped.Trim()
            : key;
    }

    /// <summary>归一抽屉键：仅接受声明面已声明的键，其余 → none（未知抽屉不得静默成为内容分支）。</summary>
    private string NormalizeDrawer(string? drawer) => FindDrawer(drawer)?.Key ?? "none";

    /// <summary>按声明查抽屉（none/空 ⇒ null）。</summary>
    private ConsoleDrawerSpec? FindDrawer(string? key)
        => string.IsNullOrWhiteSpace(key) || string.Equals(key, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : _options.Drawers.FirstOrDefault(d => string.Equals(d.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

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
