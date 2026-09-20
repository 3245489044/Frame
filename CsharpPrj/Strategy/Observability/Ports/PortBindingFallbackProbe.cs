namespace Strategy.Observability.Ports;

using System.Collections.Concurrent;

/// <summary>
/// 装配期回退探针（批 0 一次性计量；批 1 随回退路径一并退场）。
/// <para>
/// 对象 = <see cref="PortBinding.TryConstruct"/> 里「规格不可求值（<see cref="PortSpec.IsEvaluable"/> = false）
/// ⇒ 回退旧绑定类」的三处命中（reflective / composed / scripted）。计数为**计量口径**：判定该回退路径
/// 在装配面是否仍可达 —— 可达则批 1 是纯删除的前提不成立，须先为这些键补声明形态或显式登记。
/// </para>
/// <para>
/// 严格模式（环境变量 <c>PORT_FALLBACK_STRICT=1</c>）：命中即抛，异常消息含「归属/端口键」，用于在全量端口套件下
/// 一次性取得逐键名单（不写盘、不落库，避免内核承担 IO）。
/// </para>
/// </summary>
public static class PortBindingFallbackProbe
{
    /// <summary>严格模式开关名（命中即抛；缺省关闭 = 仅计数）。</summary>
    public const string StrictVariable = "PORT_FALLBACK_STRICT";

    private static readonly ConcurrentDictionary<string, byte> Hits = new(StringComparer.Ordinal);
    private static int _count;

    /// <summary>命中次数（进程内累计）。</summary>
    public static int Count => Volatile.Read(ref _count);

    /// <summary>命中名单（<c>归属/端口键</c>，Ordinal 升序；同键同归属去重）。</summary>
    public static IReadOnlyList<string> Entries
        => Hits.Keys.OrderBy(entry => entry, StringComparer.Ordinal).ToArray();

    /// <summary>登记一次回退命中（唯一登记入口）：计数 + 记名，严格模式即抛（异常消息含归属与端口键）。</summary>
    public static void Record(string portKey, string kind)
    {
        Interlocked.Increment(ref _count);
        Hits.TryAdd($"{kind}/{portKey}", 0);

        if (string.Equals(Environment.GetEnvironmentVariable(StrictVariable), "1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"[port:fallback-probe] 端口 '{portKey}' 命中归属 '{kind}' 的旧绑定回退路径（规格 IsEvaluable=false）"
                + $"；本探针严格模式（{StrictVariable}=1）据此判定该回退路径的可达性。");
    }

    /// <summary>清空计数与名单（计量基线重置用）。</summary>
    public static void Reset()
    {
        Hits.Clear();
        Interlocked.Exchange(ref _count, 0);
    }
}
