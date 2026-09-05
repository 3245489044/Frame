namespace CallCenter.Core.Cache;

/// <summary>
/// 热加载版本表（P4 核心语义）：管理单个工具的多版本（fingerprint）与在途引用计数，支撑
/// 「新版本激活 → 旧版本保留在途调用 → 旧计数归零可回收」的无损热切换。本类只做版本/引用状态机，
/// 文件监视、重编译与 CSG 一致性校验由上层（watcher/Build 轨）驱动调用本类接口。
/// 线程安全；工具名不区分大小写，fingerprint 区分大小写（十六进制摘要）。
/// </summary>
public sealed class HotReloadVersionStore
{
    private readonly Dictionary<string, ToolVersions> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>登记一个新版本（同名工具新 fingerprint 入档并成为激活版本）。</summary>
    public void RegisterVersion(string tool, string fingerprint)
    {
        lock (_gate) GetOrAdd(tool).Register(fingerprint);
    }

    /// <summary>当前激活版本的 fingerprint（同工具最后登记版本；无版本返回 null）。</summary>
    public string? ActiveFingerprint(string tool)
    {
        lock (_gate) return GetOrAdd(tool).Active;
    }

    /// <summary>该工具已登记的全部版本（含可回收旧版，登记序）。</summary>
    public IReadOnlyList<string> Versions(string tool)
    {
        lock (_gate) return GetOrAdd(tool).Order.ToArray();
    }

    /// <summary>在途调用计数 +1（旧版本在热切换后仍被在途引用）。</summary>
    public void BeginInFlight(string tool, string fingerprint)
    {
        lock (_gate) GetOrAdd(tool).Begin(fingerprint);
    }

    /// <summary>在途调用计数 -1（调用结束）。</summary>
    public void EndInFlight(string tool, string fingerprint)
    {
        lock (_gate) GetOrAdd(tool).End(fingerprint);
    }

    /// <summary>某版本当前在途计数（0 = 无在途调用）。</summary>
    public int InFlightCount(string tool, string fingerprint)
    {
        lock (_gate) return GetOrAdd(tool).InFlight.TryGetValue(fingerprint, out var c) ? c : 0;
    }

    /// <summary>某版本是否可回收：已登记、非激活、且在途计数为 0。</summary>
    public bool IsDrainable(string tool, string fingerprint)
    {
        lock (_gate) return GetOrAdd(tool).IsDrainable(fingerprint);
    }

    /// <summary>可回收版本清单（旧且无在途调用）。</summary>
    public IReadOnlyList<string> DrainableVersions(string tool)
    {
        lock (_gate)
        {
            var v = GetOrAdd(tool);
            return v.Order.Where(v.IsDrainable).ToArray();
        }
    }

    /// <summary>回收所有可回收版本（移除档案；返回移除数）。</summary>
    public int PruneDrained(string tool)
    {
        lock (_gate)
        {
            var v = GetOrAdd(tool);
            var drained = v.Order.Where(v.IsDrainable).ToArray();
            foreach (var fp in drained)
            {
                v.Order.Remove(fp);
                v.InFlight.Remove(fp);
            }

            return drained.Length;
        }
    }

    /// <summary>
    /// 整工具退役：移除该工具名的全部版本与在途档案（B2/C3 —— 用于换轨移除后该工具名不再由任何轨路由，
    /// 避免其旧版本永远残留为 Active 并污染后续同名登记/在途判断）。注意：调用方须确认已无该工具的在途调用
    ///（当前在途计数并非全局接线，属协调器驱动语义）。
    /// </summary>
    public void Remove(string tool)
    {
        lock (_gate) _tools.Remove(tool);
    }

    private ToolVersions GetOrAdd(string tool)
    {
        if (!_tools.TryGetValue(tool, out var versions))
        {
            versions = new ToolVersions();
            _tools[tool] = versions;
        }

        return versions;
    }

    private sealed class ToolVersions
    {
        public readonly List<string> Order = new();
        public readonly Dictionary<string, int> InFlight = new(StringComparer.Ordinal);

        public string? Active => Order.Count > 0 ? Order[^1] : null;

        public void Register(string fingerprint)
        {
            if (Order.Contains(fingerprint)) return;
            Order.Add(fingerprint);
            InFlight.TryAdd(fingerprint, 0);
        }

        public void Begin(string fingerprint)
        {
            if (!InFlight.ContainsKey(fingerprint))
                throw new InvalidOperationException($"版本 '{fingerprint}' 尚未登记，无法开始在途调用");
            InFlight[fingerprint]++;
        }

        public void End(string fingerprint)
        {
            if (!InFlight.TryGetValue(fingerprint, out var count) || count <= 0)
                throw new InvalidOperationException($"版本 '{fingerprint}' 无在途调用可结束");
            InFlight[fingerprint] = count - 1;
        }

        public bool IsDrainable(string fingerprint)
            => Order.Contains(fingerprint)
               && fingerprint != Active
               && InFlight.TryGetValue(fingerprint, out var count)
               && count == 0;
    }
}
