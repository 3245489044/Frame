using System.Collections.Concurrent;
using System.Text;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 子计划编译产物注册表（区域化的运行时入口）：内容指纹 → <see cref="CompiledRecipe"/>。
/// <para>
/// 口径与消费方缓存（Agent 侧轮骨架注册表）一致：**键 = 内容指纹**（同名不同内容必重建）、
/// **失败不写缓存**（下次请求重试）、**无全局锁**（同名并发首建允许重复一次，结果等值）。
/// </para>
/// <para>
/// 编译一律经框架**唯一入口** <see cref="CircuitCompiler.TryCompile(CircuitRecipeSpec, out CompiledRecipe, out IReadOnlyList{CircuitDiagnostic})"/>
/// —— 子图也必须有编译期判据，不得裸跑。
/// </para>
/// <para>
/// 指纹**递归包含嵌套子计划**：子图内容变 ⇒ 父键变 ⇒ 父级不复活陈旧子图（父子联动失效）。
/// </para>
/// </summary>
public static class CompiledRecipeRegistry
{
    private static readonly ConcurrentDictionary<string, CompiledRecipe> Cache = new(StringComparer.Ordinal);

    /// <summary>键分隔符（与指纹段分隔；避免端口名与字段名拼接产生歧义键）。</summary>
    private const char Separator = '\u001F';

    /// <summary>已缓存条数（诊断 / 门禁正对照）。</summary>
    public static int Count => Cache.Count;

    /// <summary>取（或首次编译）该配方的编译产物；编译失败按 <see cref="CircuitCompiler.Compile(CircuitRecipeSpec)"/> 语义抛出。</summary>
    public static CompiledRecipe GetOrCompile(CircuitRecipeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Cache.GetOrAdd(Fingerprint(spec), _ => CircuitCompiler.Compile(spec));
    }

    /// <summary>取（或首次编译）；失败不写缓存并回传诊断（不抛版本）。</summary>
    public static bool TryGetOrCompile(
        CircuitRecipeSpec spec,
        out CompiledRecipe? compiled,
        out IReadOnlyList<CircuitDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var key = Fingerprint(spec);
        if (Cache.TryGetValue(key, out var cached))
        {
            compiled = cached;
            diagnostics = Array.Empty<CircuitDiagnostic>();
            return true;
        }

        if (!CircuitCompiler.TryCompile(spec, out compiled, out diagnostics))
            return false; // 失败不写缓存（下次请求重试）

        Cache[key] = compiled!;
        return true;
    }

    /// <summary>清空缓存（门禁 / 测试用）。</summary>
    public static void Clear() => Cache.Clear();

    /// <summary>
    /// 内容指纹（投影输入全集；顺序敏感；**含嵌套子计划**）：覆盖 <see cref="CircuitRecipeProjector.Project"/> 的全部入参来源
    /// （电路 Id / EntryPorts / ExitPorts / 有序步骤与端口集 / Wire / 常量注入 / 控制边 / 区域声明与子计划指纹）。
    /// </summary>
    internal static string Fingerprint(CircuitRecipeSpec spec)
    {
        var sb = new StringBuilder(256);
        Append(sb, spec);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, CircuitRecipeSpec spec)
    {
        sb.Append(spec.CircuitId ?? string.Empty).Append(Separator);

        foreach (var e in spec.EntryPorts) sb.Append(e).Append(',');
        sb.Append(Separator);

        foreach (var x in spec.ExitPorts) sb.Append(x).Append(',');
        sb.Append(Separator);

        foreach (var s in spec.Steps)
        {
            sb.Append(s.Id).Append(':').Append(s.StepKind).Append(':');
            foreach (var p in s.InPorts) sb.Append(p).Append(',');
            sb.Append(':');
            foreach (var p in s.OutPorts) sb.Append(p).Append(',');
            sb.Append(';');
        }
        sb.Append(Separator);

        foreach (var f in spec.Flows)
            sb.Append(f.FromNode).Append('.').Append(f.FromPort).Append('>')
              .Append(f.ToNode).Append('.').Append(f.ToPort)
              .Append('?').Append(f.When ?? string.Empty).Append(';');
        sb.Append(Separator);

        foreach (var l in spec.Literals ?? Array.Empty<CircuitLiteralSpec>())
            sb.Append(l.NodeId).Append('.').Append(l.Port).Append('=').Append(l.Value).Append(';');
        sb.Append(Separator);

        // 控制边：投影产 Sequence 镜像 ⇒ 属投影输入，须进指纹（否则同名同内容键复用陈旧电路）
        foreach (var c in spec.ControlEdges ?? Array.Empty<CircuitControlEdgeSpec>())
            sb.Append(c.FromNode).Append('>').Append(c.ToNode).Append(';');
        sb.Append(Separator);

        // 容错策略（G4）：投影产 Retry/Fallback/TryGuard 符号 ⇒ 属投影输入，须进指纹。
        foreach (var r in spec.Retries ?? Array.Empty<CircuitRetrySpec>())
            sb.Append('r').Append(r.NodeId).Append(':').Append(r.MaxAttempts).Append(':').Append(r.BackoffBaseMs).Append(';');
        foreach (var f in spec.Fallbacks ?? Array.Empty<CircuitFallbackSpec>())
            sb.Append('f').Append(f.PrimaryNodeId).Append('>').Append(f.AlternativeNodeId).Append(';');
        foreach (var g in spec.Guards ?? Array.Empty<CircuitGuardSpec>())
        {
            sb.Append('g');
            foreach (var id in g.ProtectedNodeIds) sb.Append(id).Append(',');
            sb.Append(';');
        }
        sb.Append(Separator);

        // 区域：声明 + 子计划指纹递归（U5/R4：父键须随子图内容变化）
        foreach (var r in spec.Regions ?? Array.Empty<CircuitRegionSpec>())
        {
            sb.Append(r.NodeId).Append(':').Append(r.RegionKind).Append(':')
              .Append(r.Target?.CanonicalId ?? string.Empty).Append(':')
              .Append(r.Plan?.Kind.ToString() ?? string.Empty).Append(':')
              .Append(r.Plan?.MaxIterations?.ToString() ?? string.Empty).Append(':')
              .Append(r.Plan?.Repeats.ToString() ?? string.Empty).Append(':')
              .Append(r.Plan?.JudgePort ?? string.Empty).Append(':');

            foreach (var b in r.InBindings) sb.Append('i').Append(b.ParentPort).Append('=').Append(b.ChildPort).Append(',');
            foreach (var b in r.OutBindings) sb.Append('o').Append(b.ParentPort).Append('=').Append(b.ChildPort).Append(',');
            sb.Append('{');

            if (r.Child is not null) Append(sb, r.Child);
            sb.Append('}').Append(';');
        }
    }
}
