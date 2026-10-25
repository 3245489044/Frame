using Share.EventBindingOrchestrator.Utils;
using TConfig;

namespace Share.EventBindingOrchestrator.Binding;

/// <summary>
/// 交互声明图节点（A0）：把物理控件与 HTML 页内元素统一为「可交互节点」。
/// <para><see cref="Key"/> 为域限定键（<c>host:name</c> / <c>page:name</c>），是实体注册、事件声明索引与
/// 写回目标解析的共同键；<see cref="Events"/> 声明即开关（无声明不产声明图）。</para>
/// </summary>
public sealed record InteractionNode(
    string Key,
    string Domain,
    string Name,
    string Path,
    EventConfig? Events,
    EventBinding? Bindings,
    IReadOnlyDictionary<string, WorkflowDefinitionEntry> Workflows,
    string? Output)
{
    public bool HasInteraction => Events?.EventsDictionary is { Count: > 0 }
        || Workflows.Count > 0;
}

/// <summary>
/// 交互图投影器（A0 单源）：UIConfiguration → InteractionNode[]（host/page 两域）。
/// 只读声明，不注册、不建实体（注册落 A1），可被门禁/诊断/注册复用。
/// </summary>
public static class InteractionGraphProjector
{
    public static IReadOnlyList<InteractionNode> Project(UIConfiguration? config)
    {
        if (config?.Views == null) return Array.Empty<InteractionNode>();

        var nodes = new List<InteractionNode>();
        ControlTreeWalker.WalkAllDomains(config, (control, domain, path) =>
        {
            if (control == null || string.IsNullOrWhiteSpace(control.Name)) return;

            var bindings = control.Events?.Bindings;
            var workflows = bindings?.Workflows is { Count: > 0 } map
                ? (IReadOnlyDictionary<string, WorkflowDefinitionEntry>)map
                : new Dictionary<string, WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);

            nodes.Add(new InteractionNode(
                TConfig.InteractionDomains.Qualify(domain, control.Name),
                domain,
                control.Name,
                path,
                control.Events,
                bindings,
                workflows,
                bindings?.Output));
        });

        AppendEventTargets(config, nodes);
        return nodes;
    }

    /// <summary>页面级事件目标（R4）入图：模板动态行的具名事件（不渲染、只声明）。</summary>
    private static void AppendEventTargets(UIConfiguration config, List<InteractionNode> nodes)
    {
        if (config.Views is null) return;
        ControlTreeWalker.WalkAllDomains(config, (control, _, path) =>
        {
            if (control?.HtmlPage?.EventTargets is not { Count: > 0 } targets) return;
            var pageName = string.IsNullOrWhiteSpace(control.HtmlPage.Name) ? "page" : control.HtmlPage.Name;
            foreach (var kvp in targets)
            {
                var target = kvp.Value;
                if (target is null) continue;
                var name = string.IsNullOrWhiteSpace(target.Name) ? kvp.Key : target.Name;
                var targetBindings = target.Events?.Bindings;
                var workflows = targetBindings?.Workflows is { Count: > 0 } map
                    ? (IReadOnlyDictionary<string, WorkflowDefinitionEntry>)map
                    : new Dictionary<string, WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);
                nodes.Add(new InteractionNode(
                    TConfig.InteractionDomains.Qualify(TConfig.InteractionDomains.PageName, name),
                    TConfig.InteractionDomains.PageName,
                    name,
                    $"{path}/#{pageName}!events/{name}",
                    target.Events,
                    targetBindings,
                    workflows,
                    targetBindings?.Output));
            }
        });
    }
    /// <summary>
    /// 收集工作流声明（A1 前置）：host 先入、page 后入；同名不同域 → 冲突（fail-fast，禁静默覆盖）。
    /// 返回合并表与冲突清单（冲突时调用方必须中止注册）。
    /// </summary>
    public static Dictionary<string, WorkflowDefinitionEntry> CollectWorkflows(
        IReadOnlyList<InteractionNode> nodes,
        out List<string> conflicts)
    {
        var merged = new Dictionary<string, WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);
        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        conflicts = new List<string>();

        foreach (var node in nodes)
        {
            foreach (var kvp in node.Workflows)
            {
                if (merged.ContainsKey(kvp.Key))
                {
                    conflicts.Add($"工作流名 '{kvp.Key}' 重复声明：{origins[kvp.Key]} 与 {node.Key}");
                    continue;
                }

                merged[kvp.Key] = kvp.Value.WithFallbackIdentity(kvp.Key);
                origins[kvp.Key] = node.Key;
            }
        }

        return merged;
    }

    /// <summary>页面域交互节点（A1 注册范围）。</summary>
    public static IEnumerable<InteractionNode> PageNodes(IReadOnlyList<InteractionNode> nodes)
        => nodes.Where(n => n.Domain == TConfig.InteractionDomains.PageName);
}
