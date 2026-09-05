using System.Reflection;
using Plate.DslMethod.Abstractions;
using Plate.DslMethod.Model;

namespace Plate.DslMethod.Logic;

/// <summary>
/// 代码公理扫描器：反射扫描程序集，收集 <see cref="AxiomAttribute"/> 公理类与 <see cref="BindAxiomAttribute"/> 绑定，
/// 建公理索引与反向引用（编译期特性为单一通道）。
/// 解决「实现↔公理对应散落」：全部对应以 [Axiom]/[BindAxiom] 集中登记，可机检、可反向追溯、可治理。
/// </summary>
public sealed class AxiomScanner
{
    private readonly List<AxiomInfo> _axioms = new();
    private readonly List<AxiomBindingInfo> _bindings = new();
    private readonly Dictionary<Type, AxiomInfo> _axiomByType = new();
    private readonly Dictionary<string, AxiomInfo> _axiomByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AxiomBindingInfo>> _reverse = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ValidationIssue> _issues = new();

    public IReadOnlyList<AxiomInfo> Axioms => _axioms;

    public IReadOnlyList<AxiomBindingInfo> Bindings => _bindings;

    /// <summary>反向引用：公理名 → 绑定它的实现类清单（改公理 → 谁实现）。</summary>
    public IReadOnlyDictionary<string, List<AxiomBindingInfo>> Reverse => _reverse;

    /// <summary>扫描期间无法解析的绑定（目标公理未在已扫描程序集内注册）。</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>
    /// 扫描程序集（两遍：先收 [Axiom] 建索引，再解析 [BindAxiom]）。
    /// 绑定类与公理类常分处不同程序集（Agent.Core 绑 Plate.Axioms），故沿引用图 BFS 装载
    /// 传递引用程序集，收集其中声明 [Axiom]/[BindAxiom] 的类型，保证 [BindAxiom] target 可解析。
    /// </summary>
    public AxiomScanner Scan(params Assembly[] assemblies)
    {
        var types = new List<Type>();
        var visited = new HashSet<Assembly>();
        var queue = new Queue<Assembly>(assemblies.Where(a => a is not null));
        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            if (!visited.Add(asm)) continue;
            types.AddRange(SafeTypes(asm));
            foreach (var refName in asm.GetReferencedAssemblies())
            {
                try
                {
                    var refAsm = Assembly.Load(refName);
                    if (!visited.Contains(refAsm)) queue.Enqueue(refAsm);
                }
                catch
                {
                    // 引用程序集不可加载则跳过（不阻断扫描）
                }
            }
        }

        // pass 1：收集 [Axiom] 公理类 → 索引
        foreach (var type in types)
        {
            var ax = type.GetCustomAttribute<AxiomAttribute>();
            if (ax is null) continue;
            var info = new AxiomInfo(type, ax.Name, ax.Level, ax.Axis ?? "", ax.Description);
            _axioms.Add(info);
            _axiomByType.TryAdd(type, info);
            _axiomByName.TryAdd(ax.Name, info);
        }

        // pass 2：解析 [BindAxiom] 绑定（target 必须已在索引中）
        foreach (var type in types)
        {
            foreach (var attr in type.GetCustomAttributes<BindAxiomAttribute>())
            {
                if (!_axiomByType.TryGetValue(attr.AxiomType, out var ainfo))
                {
                    _issues.Add(new ValidationIssue(null,
                        $"[BindAxiom] {type.Name}: 目标公理类型 '{attr.AxiomType.Name}' 未在已扫描程序集内注册"));
                    continue;
                }
                var binding = new AxiomBindingInfo(
                    type, attr.AxiomType, ainfo.Name, ainfo.Level, ainfo.Axis,
                    attr.Kind, attr.Status, BindingChannel.Code);
                _bindings.Add(binding);
                _reverse.GetOrAdd(ainfo.Name).Add(binding);
            }
        }

        return this;
    }

    /// <summary>按公理类型取元数据。</summary>
    public AxiomInfo? AxiomOf(Type axiomType) => _axiomByType.TryGetValue(axiomType, out var a) ? a : null;

    /// <summary>按公理名取元数据。</summary>
    public AxiomInfo? AxiomOf(string name) => _axiomByName.TryGetValue(name, out var a) ? a : null;

    /// <summary>漂移绑定清单（status=drift）。</summary>
    public List<string> FindDrift()
        => _bindings.Where(x => x.Status == BindStatus.Drift)
            .Select(x => $"{x.DeclaringType?.Name ?? x.AxiomName}: {x.AxiomName} (drift)")
            .ToList();

    /// <summary>缺口公理（声明于契约但无一代码实现类绑定）。</summary>
    public List<string> FindGaps()
        => _axioms.Where(a => !_reverse.ContainsKey(a.Name))
            .Select(a => $"{a.Name} ({a.Level.ToDisplay()}): 无实现绑定")
            .ToList();

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
