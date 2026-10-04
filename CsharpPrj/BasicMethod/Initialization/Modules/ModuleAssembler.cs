using BasicMethod.Initialization.Errors;
using BasicMethod.Initialization.Guards;
using BasicMethod.Initialization.Modules;

namespace BasicMethod.Initialization;

/// <summary>
/// 模块初始化编排器：依赖拓扑序 + 阶段化 + 失败分类（扩展既有 <c>ILifecycleManager</c> 的顺序初始化）。
/// <para>拓扑序在此以局部 Kahn 实现（节点 = <see cref="IModuleNode"/>，边 = 名称依赖）；
/// 通用图拓扑排序位于 <c>GraphFoundation.GraphTopology</c>，但本工程零对外引用故不引用之——
/// 此处排序域为「模块装配序」，与图领域拓扑不同源。</para>
/// </summary>
public sealed class ModuleAssembler : IModuleAssembler
{
    private readonly List<IModuleNode> _nodes = new();

    /// <inheritdoc />
    public void Add(IModuleNode node)
    {
        PortGuard.Required(node, nameof(node));
        _nodes.Add(node);
    }

    /// <inheritdoc />
    public async ValueTask<AssemblyResult> AssembleAsync(CancellationToken ct)
    {
        var sorted = TopologicalSort(out var unresolved);
        var failed = new List<string>(unresolved);
        var missing = new List<string>();

        foreach (var node in sorted)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await node.InitializeAsync(ct).ConfigureAwait(false);
            }
            catch (MissingDependencyException mde)
            {
                // 装配期缺陷：缺前置端口（可诊断、可归因）。
                missing.AddRange(mde.MissingPorts);
                failed.Add(node.Name);
            }
            catch (Exception)
            {
                // 运行失败：归因到模块名（不吞并语义，交由上层账本/日志处置）。
                failed.Add(node.Name);
            }
        }

        return new AssemblyResult(failed.Count == 0 && missing.Count == 0, failed, missing);
    }

    /// <summary>依赖拓扑排序（Kahn）。未解析节点（环内）经 <paramref name="cycleNodes"/> 输出，不就地抛。</summary>
    private List<IModuleNode> TopologicalSort(out List<string> cycleNodes)
    {
        cycleNodes = new List<string>();

        var nameMap = new Dictionary<string, IModuleNode>(StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            if (!nameMap.TryAdd(node.Name, node))
                throw new InvalidOperationException($"模块名重复：{node.Name}（装配器要求唯一名称）。");
        }

        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var node in _nodes)
        {
            inDegree.TryAdd(node.Name, 0);
            foreach (var dep in node.DependsOn)
            {
                if (!nameMap.ContainsKey(dep))
                    throw new InvalidOperationException($"模块 '{node.Name}' 依赖的 '{dep}' 未注册。");

                if (!adjacency.TryGetValue(dep, out var dependents))
                {
                    dependents = new List<string>();
                    adjacency[dep] = dependents;
                }
                dependents.Add(node.Name);
                inDegree[node.Name] = inDegree.GetValueOrDefault(node.Name) + 1;
            }
        }

        var ready = inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        var sorted = new List<IModuleNode>(_nodes.Count);

        while (ready.Count > 0)
        {
            // 同层内按 Order 稳定（确定性装配序）。
            ready.Sort((a, b) => nameMap[a].Order.CompareTo(nameMap[b].Order));

            var name = ready[0];
            ready.RemoveAt(0);
            sorted.Add(nameMap[name]);

            if (adjacency.TryGetValue(name, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        ready.Add(dependent);
                }
            }
        }

        if (sorted.Count < _nodes.Count)
        {
            var sortedNames = new HashSet<string>(sorted.Select(n => n.Name), StringComparer.Ordinal);
            cycleNodes = _nodes.Select(n => n.Name).Where(n => !sortedNames.Contains(n)).ToList();
        }

        return sorted;
    }
}
