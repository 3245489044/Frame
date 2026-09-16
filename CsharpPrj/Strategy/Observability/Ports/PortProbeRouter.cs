using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口内 probe 分派（E18）：把「probe 名 → 分支体」与「可用清单」收敛为**单一事实源**——
/// 命中判定、未命中诊断与清单渲染都由本件统一，分支体由领域侧声明（本件不含领域词汇）。
/// <para>
/// 两种声明形态，用途互斥：
/// <list type="bullet">
/// <item><see cref="Of"/>（分派型）：分支体随声明给出，<see cref="Run(string)"/> 直接分派；
/// 端口实现以 <c>Run(input.Arg(0))</c> 调用 ⇒ probe 恒为第 0 个位置实参。</item>
/// <item><see cref="Declare"/>（词表型）：只声明可选值（判别留在调用点，如复合编码内的字段判别），
/// 未命中经 <see cref="Unknown"/> 抛出——清单与文案仍单源。</item>
/// </list>
/// </para>
/// <para>
/// 纪律：键非空、清单非空、清单内无空项、同键不重复声明（单一事实源）；结果清单保持声明序（诊断可读）。
/// </para>
/// </summary>
public sealed class PortProbeRouter
{
    private readonly Dictionary<string, Func<IPortInput?, object?>>? _branches;

    private PortProbeRouter(string key, IReadOnlyList<string> probes, Dictionary<string, Func<IPortInput?, object?>>? branches)
    {
        Key = key;
        Probes = probes;
        _branches = branches;
    }

    /// <summary>声明键（分派型 = 端口键；词表型 = 该词表的归属键）。</summary>
    public string Key { get; }

    /// <summary>可选值清单（声明序；可用清单唯一事实源）。</summary>
    public IReadOnlyList<string> Probes { get; }

    /// <summary>是否承载分派（<see cref="Of"/> = true，<see cref="Declare"/> = false）。</summary>
    public bool Dispatches => _branches is not null;

    /// <summary>分派型声明：键 + 「probe 名 → 分支体」（分支体不读输入时形参可忽略，见 <see cref="Run(string)"/>）。</summary>
    public static PortProbeRouter Of(
        string key, params (string Probe, Func<IPortInput?, object?> Body)[] branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        var bodies = new Dictionary<string, Func<IPortInput?, object?>>(StringComparer.Ordinal);
        var probes = new List<string>(branches.Length);

        foreach (var (probe, body) in branches)
        {
            if (string.IsNullOrWhiteSpace(probe))
                throw new ArgumentException($"[port:probe] 键 '{key}' 存在空 probe 名", nameof(branches));
            if (body is null)
                throw new ArgumentException($"[port:probe] 键 '{key}' 的 probe '{probe}' 未提供分支体", nameof(branches));
            if (!bodies.TryAdd(probe, body))
                throw new InvalidOperationException(
                    $"[port:probe] 键 '{key}' 的 probe '{probe}' 重复声明（可用清单单一事实源被破坏）");

            probes.Add(probe);
        }

        if (probes.Count == 0)
            throw new ArgumentException($"[port:probe] 键 '{key}' 未声明任何 probe", nameof(branches));

        return new PortProbeRouter(RequireKey(key), probes, bodies);
    }

    /// <summary>
    /// 词表型声明：只登记可选值（供 <see cref="Unknown"/> 渲染可用清单与守卫对拍），不承载分派。
    /// 条目可为显示文本（如 <c>static:&lt;provider&gt;</c>）——本件不解析其结构。
    /// </summary>
    public static PortProbeRouter Declare(string key, params string[] probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        foreach (var probe in probes)
            if (string.IsNullOrWhiteSpace(probe))
                throw new ArgumentException($"[port:probe] 键 '{key}' 存在空条目", nameof(probes));

        if (probes.Length == 0)
            throw new ArgumentException($"[port:probe] 键 '{key}' 未声明任何条目", nameof(probes));

        return new PortProbeRouter(RequireKey(key), probes, branches: null);
    }

    /// <summary>按 probe 名分派（分支体不读输入的形态；读输入时用 <see cref="Run(IPortInput, string)"/>）。</summary>
    public object? Run(string probe) => Run(null, probe);

    /// <summary>按 probe 名分派（输入透传给分支体）。</summary>
    public object? Run(IPortInput? input, string probe)
    {
        if (_branches is null)
            throw new InvalidOperationException(
                $"[port:probe] 键 '{Key}' 为词表型声明（无分支体），不可分派；请在调用点判别后经 Unknown 兜底");

        return _branches.TryGetValue(probe ?? string.Empty, out var body)
            ? body(input)
            : throw Unknown(probe);
    }

    /// <summary>未命中统一异常：<c>[spec:probe] 键 '{key}' 未知 probe '{probe}'（可用：…）</c>。</summary>
    public PortProbeUnknownException Unknown(string? probe)
        => new(Key, probe, Probes);

    /// <summary>
    /// 收集程序集内的路由器（静态字段声明；含非公开类型 / 非公开字段），按声明键索引——
    /// 供守卫对拍（清单 ↔ 用例）与诊断共用。键重复即抛（单一事实源）。
    /// </summary>
    public static IReadOnlyDictionary<string, PortProbeRouter> Collect(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var collected = new SortedDictionary<string, PortProbeRouter>(StringComparer.Ordinal);

        foreach (var type in PortReflection.TypesOf(assembly).OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal))
        {
            var fields = type
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType == typeof(PortProbeRouter))
                .OrderBy(f => f.Name, StringComparer.Ordinal);

            foreach (var field in fields)
            {
                if (field.GetValue(null) is not PortProbeRouter router)
                    throw new InvalidOperationException(
                        $"[port:probe] 字段 {type.FullName}.{field.Name} 未初始化（声明缺失：清单须单源且非空）");

                if (collected.TryAdd(router.Key, router)) continue;

                throw new InvalidOperationException(
                    $"[port:probe] 键 '{router.Key}' 重复声明（{type.FullName}.{field.Name}）：清单须单源");
            }
        }

        return collected;
    }

    private static string RequireKey(string key)
        => string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("[port:probe] 声明键不能为空", nameof(key))
            : key;
}
