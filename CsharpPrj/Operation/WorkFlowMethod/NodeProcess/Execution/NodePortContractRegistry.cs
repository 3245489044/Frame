using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using Modules.Node.Ports;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点端口契约宿主：节点声明键 → <see cref="PortContract"/> 的唯一投影入口。
    /// <para>
    /// A6：声明面由组合根注入（不再读进程静态 <c>KeySpaceHost</c>）；装配期预编译
    /// <c>Callkey → PortContract[]</c>，运行期零线性扫描。未注入 ⇒ <see cref="KeySpaceDeclaration.Empty"/>
    /// 并以一次诊断显式可见（不静默回落旧路径）。
    /// </para>
    /// </summary>
    public sealed class NodePortContractRegistry
    {
        private static NodePortContractRegistry _shared =
            new(KeySpaceDeclaration.Empty("workflow"));

        /// <summary>当前组合根注入的实例（兼容静态调用点；新代码应注入实例）。</summary>
        public static NodePortContractRegistry Shared => Volatile.Read(ref _shared);

        /// <summary>组合根注入声明（可多次调用；每次重建实例并清空契约缓存）。</summary>
        public static void Configure(IKeySpaceDeclaration declaration)
            => Volatile.Write(ref _shared, new NodePortContractRegistry(declaration));

        // 缓存与投影单源 = Modules.Node.Ports.NodePortContractCatalog（实例级，不读进程静态）。
        private readonly NodePortContractCatalog _catalog;

        public NodePortContractRegistry(IKeySpaceDeclaration declaration)
        {
            Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
            _catalog = new NodePortContractCatalog(declaration);
        }

        public IKeySpaceDeclaration Declaration { get; }

        /// <summary>兼容静态调用点：委托当前 <see cref="Shared"/> 实例。</summary>
        public static IReadOnlyList<PortContract> ForNode(NodeConfig? config)
            => Shared.Resolve(config);

        /// <summary>解析节点契约（按 callkey + 原始键集合缓存；同一节点第二次执行零解析）。</summary>
        public IReadOnlyList<PortContract> Resolve(NodeConfig? config)
        {
            if (config?.funcPipelineConfig is null)
            {
                return Array.Empty<PortContract>();
            }

            // 归一键集后再交给通用目录：缓存键由目录按「callKey | 入键集 | 出键集」单源构造。
            var parameters = NodeResultExtractor.NormalizeResultKeys(config.funcPipelineConfig.ParameterKeys)
                .ToArray();
            var results = NodeResultExtractor.NormalizeResultKeys(config.funcPipelineConfig.ResultKeys)
                .ToArray();
            return _catalog.Resolve(config.basicConfig?.Callkey, parameters, results);
        }
    }
}
