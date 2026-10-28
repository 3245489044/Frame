using Operation.HierarchyMethod.History;

namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// history 会话编排统一门面默认实现（LLM-free 组合根）——只编排：
/// 运行态聚合视图（经 IHistoryRuntimePort 只读投影）、批量控制（按 history_owns_task 作用域委托 IHistoryRuntimePort）、
/// 装配声明（HistoryPromptProfile 读写，委托注入 store，未注入回退内存）。
/// 装配可空：未装配关系端口 → 视图空集/控制 0；未装配运行端口 → 控制 no-op（对齐「未装配返回诊断」模式）。
/// 只编排不复制任务池/会话管线推进逻辑（SingleImplementation / TH-12）。
/// </summary>
public sealed class HistoryRuntimeService : IHistoryRuntimeService
{
    private readonly IHistoryRelationManager? _relations;
    private readonly IHistoryRuntimePort? _port;
    private readonly Func<string, CancellationToken, Task<HistoryPromptProfile?>>? _loadProfile;
    private readonly Func<string, HistoryPromptProfile, CancellationToken, Task>? _saveProfile;
    private readonly Dictionary<string, HistoryPromptProfile> _memoryProfiles = new(StringComparer.Ordinal);

    public HistoryRuntimeService(
        IHistoryRelationManager? relations = null,
        IHistoryRuntimePort? port = null,
        Func<string, CancellationToken, Task<HistoryPromptProfile?>>? loadProfile = null,
        Func<string, HistoryPromptProfile, CancellationToken, Task>? saveProfile = null)
    {
        _relations = relations;
        _port = port;
        _loadProfile = loadProfile;
        _saveProfile = saveProfile;
    }

    /// <summary>是否已装配运行端口（供调用方判空诊断）。</summary>
    public bool IsAttached => _port is not null;

    public async Task<HistoryRuntimeView> GetOverviewAsync(string historyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        var hid = historyId.Trim();
        var keys = await GetOwnedKeysAsync(hid, ct).ConfigureAwait(false);

        var statuses = new List<HistoryTaskRunStatus>(keys.Count);
        var running = 0; var paused = 0; var done = 0; var error = 0; var active = 0;

        foreach (var key in keys)
        {
            HistoryTaskRunStatus st;
            if (_port is not null)
                st = await _port.GetStatusAsync(key, ct).ConfigureAwait(false) ?? new HistoryTaskRunStatus(key);
            else
                st = new HistoryTaskRunStatus(key);

            statuses.Add(st);
            if (st.Active) active++;
            switch (st.Status?.Trim().ToLowerInvariant())
            {
                case "running": running++; break;
                case "paused": paused++; break;
                case "completed" or "done": done++; break;
                case "faulted" or "error": error++; break;
            }
        }

        return new HistoryRuntimeView(hid, keys.Count, active, running, paused, done, error, statuses);
    }

    public async Task<int> StartAsync(string historyId, HistoryPromptProfile profile, string? input = null, IReadOnlyList<string>? taskKeys = null, CancellationToken ct = default)
    {
        if (_port is null) return 0;
        var keys = await ResolveScopedKeysAsync(historyId, taskKeys, ct).ConfigureAwait(false);
        var n = 0;
        foreach (var key in keys)
            if (await _port.StartAsync(key, profile, input, ct).ConfigureAwait(false)) n++;
        return n;
    }

    public Task<int> PauseAsync(string historyId, string? taskKey = null, CancellationToken ct = default)
        => ControlAllAsync(historyId, taskKey, ct, static (port, k, c) => port.PauseAsync(k, c));

    public Task<int> ResumeAsync(string historyId, string? taskKey = null, CancellationToken ct = default)
        => ControlAllAsync(historyId, taskKey, ct, static (port, k, c) => port.ResumeAsync(k, c));

    public Task<int> CancelAsync(string historyId, string? taskKey = null, CancellationToken ct = default)
        => ControlAllAsync(historyId, taskKey, ct, static (port, k, c) => port.CancelAsync(k, c));

    public async Task<bool> StepAsync(string historyId, string taskKey, string? input = null, CancellationToken ct = default)
    {
        if (_port is null) return false;
        // 作用域守卫：step 目标必须归拢于该 history（history_owns_task），避免越权控制其它 history 的任务。
        var keys = await ResolveScopedKeysAsync(historyId, new[] { taskKey }, ct).ConfigureAwait(false);
        return keys.Count == 1 && await _port.StepAsync(taskKey, input, ct).ConfigureAwait(false);
    }

    public Task SetProfileAsync(string historyId, HistoryPromptProfile profile, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        var hid = historyId.Trim();
        if (_saveProfile is not null)
            return _saveProfile(hid, profile, ct);
        lock (_memoryProfiles) _memoryProfiles[hid] = profile;
        return Task.CompletedTask;
    }

    public async Task<HistoryPromptProfile?> GetProfileAsync(string historyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return null;
        var hid = historyId.Trim();
        if (_loadProfile is not null)
            return await _loadProfile(hid, ct).ConfigureAwait(false);
        lock (_memoryProfiles) return _memoryProfiles.TryGetValue(hid, out var p) ? p : null;
    }

    /// <summary>该 history 归拢的任务键集（未装配关系端口 → 空集）。</summary>
    private async Task<IReadOnlyList<string>> GetOwnedKeysAsync(string historyId, CancellationToken ct)
        => _relations is null
            ? Array.Empty<string>()
            : await _relations.GetTaskKeysAsync(historyId, ct).ConfigureAwait(false);

    /// <summary>解析作用域任务键集：taskKeys 空 = 全部归拢任务；否则过滤为归拢内子集。</summary>
    private async Task<IReadOnlyList<string>> ResolveScopedKeysAsync(string historyId, IReadOnlyList<string>? taskKeys, CancellationToken ct)
    {
        var owned = await GetOwnedKeysAsync(historyId, ct).ConfigureAwait(false);
        if (taskKeys is null || taskKeys.Count == 0) return owned;
        return taskKeys.Where(owned.Contains).ToList();
    }

    /// <summary>按作用域批量执行单 task 控制原语，返回成功计数（未装配运行端口 → 0）。</summary>
    private async Task<int> ControlAllAsync(
        string historyId, string? taskKey, CancellationToken ct,
        Func<IHistoryRuntimePort, string, CancellationToken, Task<bool>> control)
    {
        if (_port is null) return 0;
        var keys = string.IsNullOrWhiteSpace(taskKey)
            ? await ResolveScopedKeysAsync(historyId, null, ct).ConfigureAwait(false)
            : await ResolveScopedKeysAsync(historyId, new[] { taskKey }, ct).ConfigureAwait(false);
        var n = 0;
        foreach (var k in keys)
            if (await control(_port, k, ct).ConfigureAwait(false)) n++;
        return n;
    }
}
