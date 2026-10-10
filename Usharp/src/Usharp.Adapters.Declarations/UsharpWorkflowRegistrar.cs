using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GraphFramework.Models;
using Usharp.Emit;
using Usharp.Semantics;
using Usharp.Syntax;
using WorkFlowMethod.Orchestration.Content;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 工作流登记端口 <see cref="IWorkflowRegistrar"/> 的 USharp 投影实现：
/// 把 <c>.ushar</c> 的 <c>[Flow]</c>/<c>[Node]</c> 声明接入工作流引擎的登记端口，
/// 使「按工作流枚举节点键」成为**声明面事实**（供引用图声明 seed 等 Agent 侧消费）。
/// <para>权威链 = <c>.ushar</c> 声明（本类）→ 工作流登记端口；只读投影，不承载存储写面。</para>
/// </summary>
public sealed class UsharpWorkflowRegistrar : IWorkflowRegistrar
{
    private readonly Dictionary<string, Chain<string, NodeConfig>> _workflows = new(StringComparer.Ordinal);

    /// <summary>由已编译声明构建（每个 <c>[Flow]</c> 登记为一条链）。</summary>
    public UsharpWorkflowRegistrar(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        foreach (var flow in compilation.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId)) continue;
            _workflows[flow.FlowId] = BuildChain(flow);
        }
    }

    /// <summary>从配置根装载（<c>nodes/*.ushar</c> + <c>workflows/*.ushar</c>）并编译。</summary>
    public static UsharpWorkflowRegistrar FromConfigDirectory(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        var documents = new List<(string Path, string Text)>();
        AppendDirectory(documents, Path.Combine(configRoot, "nodes"));
        AppendDirectory(documents, Path.Combine(configRoot, "workflows"));
        return new UsharpWorkflowRegistrar(UsharpCompilation.Compile(documents));
    }

    /// <summary>从配置根装载（配置根缺省 ⇒ null，调用方回退无登记端口）。</summary>
    public static UsharpWorkflowRegistrar? TryFromConfigDirectory(string? configRoot)
        => string.IsNullOrWhiteSpace(configRoot) ? null : FromConfigDirectory(configRoot!);

    /// <inheritdoc />
    public void RegisterWorkflow(string workflowId, Chain<string, NodeConfig> chain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        _workflows[workflowId] = chain ?? throw new ArgumentNullException(nameof(chain));
    }

    /// <inheritdoc />
    public Chain<string, NodeConfig> GetWorkflow(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        _workflows.TryGetValue(workflowId, out var chain);
        return chain;
    }

    /// <inheritdoc />
    public bool UnregisterWorkflow(string workflowId)
        => !string.IsNullOrWhiteSpace(workflowId) && _workflows.Remove(workflowId);

    /// <inheritdoc />
    public IReadOnlyList<string> GetWorkflowIds() => _workflows.Keys.ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> GetNodeKeys(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId)) return Array.Empty<string>();
        if (!_workflows.TryGetValue(workflowId, out var chain) || chain?.AllNodes is null)
            return Array.Empty<string>();

        var keys = new List<string>(chain.AllNodes.Count);
        foreach (var node in chain.AllNodes)
        {
            var key = node?.basicConfig?.Callkey;
            if (string.IsNullOrWhiteSpace(key)) key = node?.Symbol;
            if (string.IsNullOrWhiteSpace(key)) key = node?.Id;
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key!);
        }
        return keys;
    }

    /// <summary><c>[Flow]</c> 步骤键 → 链节点（节点键 = 步骤键；语义最小，仅承载键事实）。</summary>
    private static Chain<string, NodeConfig> BuildChain(UsharpFlowDecl flow)
    {
        var nodes = new List<NodeConfig>(flow.Steps.Count);
        foreach (var step in flow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Key)) continue;
            nodes.Add(new NodeConfig
            {
                Id = step.Key,
                Symbol = step.Key,
                basicConfig = new NodeConfig.BasicConfig { Callkey = step.Key },
            });
        }
        return new Chain<string, NodeConfig>(flow.FlowId, nodes);
    }

    private static void AppendDirectory(List<(string Path, string Text)> sink, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.ushar", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
            sink.Add((file, File.ReadAllText(file)));
    }
}
