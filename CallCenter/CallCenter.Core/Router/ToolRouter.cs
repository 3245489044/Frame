using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;

namespace CallCenter.Core.Router;

/// <summary>
/// 控制面 Router：把 <see cref="ICallableTrack"/>（终端面各轨）聚合为一张统一可调用工具面。上层只向它
/// 提交工具名（或 <c>csg://tool/{name}</c> id）与 JSON 入参；Router 做 <c>Lookup → 路由到对应轨 → 执行 → 回填</c>，
/// 只操作 <see cref="ToolDescriptor"/> 与 JSON，不感知语言（P0/P1 网关：Agent 工具面的唯一 <c>_toolProvider</c> 落点）。
/// <para>
/// 工具面（<see cref="Tools"/>/按名 Lookup）暴露"每名一条激活轨"（同名多轨按登记序取先，P4 VersionTable 在此做版本化切换）；
/// 而 <c>tool://…</c> id 索引保留全部轨道版本，供按 id 精确定位。
/// </para>
/// </summary>
public sealed class ToolRouter
{
    private readonly List<ICallableTrack> _tracks = new();

    // 激活轨（每工具名一条，登记序取先）→ 上层可见的唯一工具面。
    private readonly Dictionary<string, ToolDescriptor> _activeByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ToolDescriptor> _activeOrder = new();

    // 完整 id 索引（tool:{backend}:{name}）→ 保留多轨/多版本，供按 id 精确定位。
    private readonly Dictionary<string, ToolDescriptor> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登记一条轨并并入工具面。同名工具由首次登记轨成为激活实现（先到先得，版本化见 P4）。</summary>
    public void AddTrack(ICallableTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _tracks.Add(track);
        RebuildIndex();
    }

    /// <summary>
    /// P4 热加载：以「同 backend 的新轨」替换该 backend 的旧轨并重建索引。替换后上层按 name 命中即用新轨实现
    ///（同名工具活性实现原子切到新版本）；旧轨被移除，其 delegate 若仍有在途调用由调用方持有的实例继续完成
    ///（本索引只做路由/元数据，不持执行体），无在途后经 <see cref="Cache.HotReloadVersionStore"/> 回收版本元数据。
    /// 仅替换 <paramref name="newTrack"/> 所属 backend 的轨，其余轨不受影响。
    /// </summary>
    public void ReplaceTrack(ICallableTrack newTrack)
    {
        ArgumentNullException.ThrowIfNull(newTrack);

        _tracks.RemoveAll(t => string.Equals(t.Backend, newTrack.Backend, StringComparison.OrdinalIgnoreCase));
        _tracks.Add(newTrack);
        RebuildIndex();
    }

    /// <summary>重建活性索引（_byId/_activeByName/_activeOrder），登记序取先——AddTrack 与 ReplaceTrack 共用（A3 收敛）。</summary>
    private void RebuildIndex()
    {
        _activeByName.Clear();
        _activeOrder.Clear();
        _byId.Clear();
        foreach (var t in _tracks)
        {
            foreach (var tool in t.Tools)
            {
                _byId.TryAdd(tool.Id, tool);
                if (_activeByName.TryAdd(tool.Name, tool))
                    _activeOrder.Add(tool);
            }
        }
    }

    /// <summary>激活工具面：每名一条（供 Function schema 派生，避免同名多轨重复暴露给上层）。</summary>
    public IReadOnlyCollection<ToolDescriptor> Tools => _activeOrder;

    /// <summary>按名（激活轨）或完整 id / <c>csg://tool/…</c> 解析工具描述符。</summary>
    public bool TryResolve(string nameOrId, out ToolDescriptor descriptor)
    {
        var key = nameOrId.StartsWith("csg://tool/", StringComparison.OrdinalIgnoreCase)
            ? nameOrId["csg://tool/".Length..]
            : nameOrId;

        if (_activeByName.TryGetValue(key, out descriptor!)) return true;
        return _byId.TryGetValue(key, out descriptor!);
    }

    /// <summary>是否有任一条轨能按名调用。</summary>
    public bool CanCall(string name)
        => _tracks.Any(t => t.CanCall(name));

    /// <summary>按名路由到可调用轨执行。未知工具 / 无轨可调 → 失败结果。</summary>
    public async Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
    {
        foreach (var track in _tracks)
        {
            if (track.CanCall(name))
                return await track.CallAsync(name, argumentsJson, ct).ConfigureAwait(false);
        }

        return ToolExecutionResult.Fail($"Router 中无可执行工具 '{name}'（未发现或不可调用）");
    }
}
