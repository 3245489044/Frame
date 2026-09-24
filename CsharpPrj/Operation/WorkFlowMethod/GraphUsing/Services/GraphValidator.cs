#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFramework;
using GraphFramework.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Snapshots;
#endregion

namespace WorkFlowMethod.GraphFramework.Services
{
    public class GraphValidator
    {
        /// <summary>
        /// 链连通性校验（环 / 悬空 / 键重复）：判据唯一算法 = 框架
        /// <see cref="Modules.Node.Spec.NodeSpecValidator.ValidateChains"/>（治 P19，与语言侧同一图后端）。
        /// 链为 null → 失败；链级违规 → 失败（消息聚合）。
        /// </summary>
        public static Infrastructure.ValidationResult ValidateChainConnectivity(Chain<string, NodeConfig> chain)
        {
            if (chain == null)
                return Infrastructure.ValidationResult.Failure("链为 null");

            var report = NodeChainDocuments.ChainValidator.ValidateChains(
                new[] { NodeChainDocuments.FromChain(chain) },
                requireEntry: false,
                requireExit: false);

            return report.Errors.Count == 0
                ? Infrastructure.ValidationResult.Success()
                : Infrastructure.ValidationResult.Failure(string.Join("; ", report.Errors.Select(violation => violation.Message)));
        }

        public static Infrastructure.ValidationResult Validate(IGraph<string, NodeConfig, object> graph)
        {
            if (graph == null)
                return Infrastructure.ValidationResult.Failure("图为 null");

            var result = GraphFrameworkUsage.ValidateGraph(graph);

            return result.IsValid
                ? Infrastructure.ValidationResult.Success()
                : Infrastructure.ValidationResult.Failure(result.ErrorMessage ?? "图验证失败");
        }

        public static Infrastructure.ValidationResult ValidateSnapshot(GraphSnapshot<string, NodeConfig, object> snapshot)
        {
            if (snapshot == null)
                return Infrastructure.ValidationResult.Failure("快照为 null");

            snapshot.Validate(out var errors);
            return errors.Count == 0
                ? Infrastructure.ValidationResult.Success()
                : Infrastructure.ValidationResult.Failure(string.Join("; ", errors));
        }
    }
}
