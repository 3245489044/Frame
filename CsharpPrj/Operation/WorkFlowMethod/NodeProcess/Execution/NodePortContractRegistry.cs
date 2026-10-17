using System;
using System.Collections.Generic;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using Modules.KeySpace.Runtime;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点端口契约宿主：节点声明键 → <see cref="PortContract"/> 的唯一投影入口。
    /// <para>声明源 = <see cref="KeySpaceHost"/>（组合根注入）；未注入 ⇒ 空契约（回落 NodePortRules）。</para>
    /// </summary>
    public static class NodePortContractRegistry
    {
        public static IReadOnlyList<PortContract> ForNode(NodeConfig? config)
        {
            var declaration = KeySpaceHost.Declaration;
            if (declaration is null || config?.funcPipelineConfig is null)
            {
                return Array.Empty<PortContract>();
            }

            var parameterKeys = NodeResultExtractor.NormalizeResultKeys(config.funcPipelineConfig.ParameterKeys);
            var resultKeys = NodeResultExtractor.NormalizeResultKeys(config.funcPipelineConfig.ResultKeys);
            return PortContractRules.ForKeys(declaration, parameterKeys, resultKeys);
        }
    }
}
