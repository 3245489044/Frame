#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFramework;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Snapshots;
#endregion

namespace WorkFlowMethod.GraphFramework.Services
{
    public class GraphValidator
    {
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
