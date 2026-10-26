using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.Wiring.Abstractions;
using Modules.Wiring.Declaration;
using Modules.Wiring.Model;
using WorkFlowMethod.KeySpace.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using ChainModel = GraphFramework.Models.Chain<string, WorkFlowMethod.Skeletons.NodeProcess.Models.NodeConfig>;
using KeySpacePortDirection = Modules.KeySpace.Model.PortDirection;
using WiringPortDirection = Modules.Wiring.Model.PortDirection;

namespace WorkFlowMethod.Skeletons.NodeProcess.Wiring;

/// <summary>
/// B1：工作流链 → <see cref="WiringCatalog"/> 的声明投影（层 4 适配器）。
/// <para>
/// 盒 = 节点；端口 = ParameterKeys(In) / ResultKeys(Out)；边 = 相邻节点间同名端口
/// （重命名已在构建期由 KeyMap 写入 ParameterKeys，故同名匹配与既有语义一致）。
/// 运行顺序仍归 Pipeline/[Flow]，本投影不承载顺序。
/// </para>
/// </summary>
public static class WorkflowWiringCatalogBuilder
{
    public const string FormId = "json";

    public static WiringCatalog Build(
        string sourceId,
        ChainModel? chain,
        IKeySpaceDeclaration? declaration = null)
    {
        declaration ??= WorkflowKeySpaceDefaults.Declaration;
        var nodes = chain?.AllNodes?.ToList() ?? new List<NodeConfig>();
        var boxes = new List<BoxSpec>(nodes.Count);

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var callkey = node.basicConfig?.Callkey ?? node.Id ?? $"node{index}";
            var parameters = NodeResultExtractor.NormalizeResultKeys(node.funcPipelineConfig?.ParameterKeys).ToArray();
            var results = NodeResultExtractor.NormalizeResultKeys(node.funcPipelineConfig?.ResultKeys).ToArray();

            var ports = new List<PortSpec>(parameters.Length + results.Length);
            ports.AddRange(parameters.Select(name => new PortSpec(
                callkey, name, WiringPortDirection.In, FormId,
                RequiredOf(declaration, name, KeySpacePortDirection.In))));
            ports.AddRange(results.Select(name => new PortSpec(
                callkey, name, WiringPortDirection.Out, FormId,
                RequiredOf(declaration, name, KeySpacePortDirection.Out))));

            var impl = node.funcPipelineConfig?.FuncName
                       ?? node.basicConfig?.DirectString
                       ?? callkey;
            boxes.Add(new BoxSpec(callkey, impl, ports, Terminal: index == nodes.Count - 1));
        }

        var wires = new List<WireSpec>();
        for (var index = 0; index < nodes.Count - 1; index++)
        {
            var sourceNode = nodes[index];
            var targetNode = nodes[index + 1];
            var sourceKey = sourceNode.basicConfig?.Callkey ?? sourceNode.Id ?? $"node{index}";
            var targetKey = targetNode.basicConfig?.Callkey ?? targetNode.Id ?? $"node{index + 1}";

            var sourceOut = NodeResultExtractor.NormalizeResultKeys(sourceNode.funcPipelineConfig?.ResultKeys)
                .ToHashSet(StringComparer.Ordinal);
            var targetIn = NodeResultExtractor.NormalizeResultKeys(targetNode.funcPipelineConfig?.ParameterKeys)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var port in sourceOut.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!targetIn.Contains(port)) continue; // 无同名入端口 ⇒ 不生成隐式连接（W3）
                var id = $"{sourceKey}.{port}->{targetKey}.{port}";
                wires.Add(new WireSpec(
                    id,
                    $"{sourceKey}.{port}",
                    $"{targetKey}.{port}",
                    OnMissing: OnMissing.Report));
            }
        }

        var declarationSource = new WiringDeclaration(
            sourceId,
            Array.Empty<FormSpec>(),
            boxes,
            wires,
            Array.Empty<AdapterSpec>(),
            Array.Empty<ProfileSpec>());

        return WiringCatalogFactory.FromSource(declarationSource);
    }

    private static bool RequiredOf(IKeySpaceDeclaration declaration, string name, KeySpacePortDirection direction)
        => declaration.Ports.FirstOrDefault(port =>
               port.Direction == direction && string.Equals(port.Name, name, StringComparison.Ordinal))
           ?.Required ?? false;
}
