using CallCenter.Core.Router;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;

namespace CallCenter.Core.Cache;

/// <summary>一次热加载交换的结果。</summary>
public sealed record TrackHotReloadResult(
    string Backend,
    string ContentFingerprint,
    bool Swapped,
    int ToolCount);

/// <summary>
/// P4-a 热加载编排协调器：把「监视→重发现产物」接到激活闭环（RegisterVersion → <see cref="ToolRouter.ReplaceTrack"/>
/// → 在途归零回收 <see cref="HotReloadVersionStore.PruneDrained"/>）。文件监视与重编译本身（watcher / Build 轨）由上层
/// 负责，本协调器只做确定性的「换轨 + 版本登记 + 回收」编排。
/// <para>
/// 语义：同一 backend 以新的内容指纹（content fingerprint）提交一个重建的轨，才触发交换；指纹未变则跳过。
/// 交换后对每个工具名登记新版本（版本键 = {contentFingerprint}#{toolName}），并回收该名旧的、已无在途调用的版本。
/// 在途 BeginInFlight/EndInFlight 计数由调用方（真实执行处）按版本 store 语义驱动；只要旧版仍在途，<see cref="PruneDrained"/>
/// 天然不回收，归零后才回收——即「新版本激活 → 旧在途不破坏 → 归零回收」的无损切换。
/// </para>
/// </summary>
public sealed class TrackHotReloader
{
    private readonly ToolRouter _router;
    private readonly HotReloadVersionStore _versions;
    private readonly Dictionary<string, string> _backendFingerprint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _backendToolNames = new(StringComparer.OrdinalIgnoreCase);

    public TrackHotReloader(ToolRouter router, HotReloadVersionStore? versions = null)
    {
        _router = router;
        _versions = versions ?? new HotReloadVersionStore();
    }

    /// <summary>协调器使用的版本表（可在真实执行处以之做 Begin/End 在途计数）。</summary>
    public HotReloadVersionStore Versions => _versions;

    /// <summary>
    /// 提交一次热加载：内容指纹与当前激活相同 → 跳过（<see cref="TrackHotReloadResult.Swapped"/>=false）；
    /// 否则以 <paramref name="buildNewTrack"/> 产出的新轨替换同 backend 旧轨、登记新版本并回收可回收旧版。
    /// </summary>
    public TrackHotReloadResult Reload(string backend, string contentFingerprint, Func<ICallableTrack> buildNewTrack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint);
        ArgumentNullException.ThrowIfNull(buildNewTrack);

        if (_backendFingerprint.TryGetValue(backend, out var last) && last == contentFingerprint)
            return new TrackHotReloadResult(backend, contentFingerprint, Swapped: false, ToolCount: 0);

        var track = buildNewTrack();
        _router.ReplaceTrack(track);

        var tools = track.Tools.ToArray();

        // B2/C3：换轨后移除的工具名（旧轨有、新轨无）整名退役——旧版本不再残留为 Active，
        // 避免污染后续同名登记 / 在途判断（HotReloadVersionStore.Remove）。
        if (_backendToolNames.TryGetValue(backend, out var priorNames))
        {
            var current = new HashSet<string>(tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var prior in priorNames)
                if (!current.Contains(prior))
                    _versions.Remove(prior);
        }

        foreach (var tool in tools)
        {
            var versionKey = $"{contentFingerprint}#{tool.Name}";
            _versions.RegisterVersion(tool.Name, versionKey);
            // 新版本已激活：同名的旧版本（若有）成为非激活；无在途则可回收，有在途则归零后由调用方再次触发回收。
            _versions.PruneDrained(tool.Name);
        }

        _backendToolNames[backend] = tools.Select(t => t.Name).ToArray();
        _backendFingerprint[backend] = contentFingerprint;
        return new TrackHotReloadResult(backend, contentFingerprint, Swapped: true, ToolCount: tools.Length);
    }
}
