using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>entry-check</c>：**产品入口形态判据**（离线反射，非运行时面）—— 对台账条目检查其目标方法
/// 是否满足「声明面可绑定」的入口形态：① <c>public static</c>；② 无委托 / 无 <c>out·ref</c> / 无泛型形参；
/// ③ 非 <c>async</c>（无 <c>AsyncStateMachine</c>）；④ 有返回值；⑤ 实参个数存在可匹配重载（D-9 风险提示）。
/// <para>
/// <c>--kind</c>：<c>reflective</c>（缺省）只判已切反射绑定的条目；<c>declared</c> 判**凡声明了
/// <c>target</c> + <c>method</c> 的条目（不论 <c>kind</c>）** —— 支撑「产品入口已补、分片已写
/// <c>target</c>/<c>method</c>、<c>kind</c> 尚未切换」的先验证后落盘（缺省模式对未切换条目零扫描，
/// 直接用于预检会得到「扫描 0 键 / error 0」的假绿）。
/// </para>
/// <para>
/// 定位：把「产品侧缺语义化观测入口」从**事后报错**提前为**离线可查**；类型 / 程序集未命中
/// （<c>--bin</c> 面不全或目标在别的程序集）记 <c>info</c>，不计 error（离线工具不拥有全量装配面）。
/// </para>
/// <para>政策（严重度 / 文案）单源 = 数据视图 <c>entry</c> 段；本件零端口键、零领域词。</para>
/// </summary>
internal static class EntryCheckCommand
{
    /// <param name="kind">条目筛选面：<c>reflective</c>（缺省）/ <c>declared</c>（预检未切 kind 的已声明条目）。</param>
    internal static int Run(LedgerView view, string binDirectory, string? only, string? kind)
    {
        if (!Directory.Exists(binDirectory))
            throw new DirectoryNotFoundException(string.Format(view.Message("forgeDirectoryMissing"), binDirectory));

        var declared = string.Equals(kind, "declared", StringComparison.Ordinal);
        var policy = EntryPolicy.Load(view.DataPath);
        var issues = new List<EntryIssue>();
        var keys = 0;
        var resolver = new AssemblyProbe(binDirectory);

        foreach (var group in view.Groups)
        {
            var shardPath = Path.Combine(view.PartsDirectory, group.File);
            if (!File.Exists(shardPath))
                throw new InvalidOperationException(string.Format(view.Message("missingShard"), shardPath, group.Name));

            using var document = JsonDocument.Parse(File.ReadAllText(shardPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, view.ShardCommentKey, StringComparison.Ordinal)) continue;
                if (only is { Length: > 0 } filter && !property.Name.StartsWith(filter, StringComparison.Ordinal)) continue;
                if (!property.Value.TryGetProperty("impl", out var impl) || impl.ValueKind != JsonValueKind.Object)
                    continue;
                if (!Matches(impl, declared)) continue;

                keys++;
                Check(view, policy, resolver, property.Name, impl, issues);
            }
        }

        var errors = issues.Count(issue => issue.Severity == "error");
        var warnings = issues.Count(issue => issue.Severity == "warn");
        var infos = issues.Count(issue => issue.Severity == "info");

        Console.WriteLine(string.Format(
            view.Message(declared ? "entryHeaderDeclared" : "entryHeader"),
            keys, resolver.LoadedCount, Path.GetFileName(binDirectory)));

        foreach (var issue in issues
                     .OrderBy(item => item.PortKey, StringComparer.Ordinal)
                     .ThenBy(item => item.Rule, StringComparer.Ordinal))
            Console.WriteLine(string.Format(view.Message("entryRow"), issue.Severity, issue.PortKey, issue.Rule, issue.Detail));

        Console.WriteLine(string.Format(view.Message("entrySummary"), errors, warnings, infos));
        Console.WriteLine(view.Message("entryHint"));
        return errors == 0 ? 0 : 1;
    }

    /// <summary>条目筛选：<paramref name="declared"/> 只要求声明了 target + method（不论 kind），否则须 kind=reflective。</summary>
    private static bool Matches(JsonElement impl, bool declared)
        => declared
            ? Text(impl, "target") is { Length: > 0 } && Text(impl, "method") is { Length: > 0 }
            : string.Equals(Text(impl, "kind"), "reflective", StringComparison.Ordinal);

    /// <summary>逐键检查（E1–E5；文案经策略单源）。</summary>
    private static void Check(
        LedgerView view, EntryPolicy policy, AssemblyProbe resolver, string portKey, JsonElement impl,
        List<EntryIssue> issues)
    {
        void Report(string rule, params object[] args)
            => issues.Add(new EntryIssue(portKey, rule, policy.SeverityOf(rule), policy.Text(rule, args)));

        var target = Text(impl, "target");
        var method = Text(impl, "method");
        if (target is not { Length: > 0 } || method is not { Length: > 0 })
        {
            Report("entry-target-missing");
            return;
        }

        var type = resolver.Resolve(target);
        if (type is null)
        {
            Report("entry-unresolved", target);
            return;
        }

        var declared = impl.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
            ? args.GetArrayLength()
            : 0;

        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => string.Equals(candidate.Name, method, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            Report("entry-static-missing", target, method);
            return;
        }

        var chosen = candidates.FirstOrDefault(candidate => candidate.GetParameters().Length == declared) ?? candidates[0];

        if (!chosen.IsStatic) Report("entry-static-missing", target, method);
        if (chosen.GetCustomAttribute<AsyncStateMachineAttribute>() is not null) Report("entry-async", target, method);
        if (chosen.ReturnType == typeof(void)) Report("entry-void", $"{target}.{method}");
        if (typeof(Delegate).IsAssignableFrom(chosen.ReturnType)) Report("entry-delegate", $"{target}.{method}");

        foreach (var parameter in chosen.GetParameters())
        {
            var name = parameter.Name ?? "?";
            if (parameter.ParameterType.IsByRef || parameter.IsOut) Report("entry-byref", name);
            if (typeof(Delegate).IsAssignableFrom(parameter.ParameterType)) Report("entry-delegate", name);
            if (parameter.ParameterType.IsGenericParameter) Report("entry-generic", name);
        }

        if (declared != chosen.GetParameters().Length) Report("entry-arity-mismatch", declared, target, method);
    }

    private static string? Text(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// 程序集探测（离线反射面）：加载 <c>--bin</c> 目录下全部可加载程序集并提供类型全名解析。
    /// 加载失败件静默跳过（离线 bin 面可能含原生 / 版本冲突件），解析未命中由调用方记 <c>info</c>。
    /// </summary>
    private sealed class AssemblyProbe
    {
        private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

        internal AssemblyProbe(string binDirectory)
        {
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var candidate = Path.Combine(binDirectory, name.Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };

            foreach (var path in Directory.EnumerateFiles(binDirectory, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(path);
                    foreach (var type in assembly.GetTypes())
                        if (type.FullName is { Length: > 0 } fullName)
                            _types.TryAdd(fullName, type);
                }
                catch (Exception)
                {
                    // 离线探测：不可加载件跳过，不影响其余解析（未命中由 entry-unresolved 记 info）
                }
            }
        }

        /// <summary>已成功索引的类型数（诊断面：判定「bin 面是否可用」）。</summary>
        internal int LoadedCount => _types.Count;

        internal Type? Resolve(string typeName) => _types.TryGetValue(typeName, out var type) ? type : null;
    }
}
