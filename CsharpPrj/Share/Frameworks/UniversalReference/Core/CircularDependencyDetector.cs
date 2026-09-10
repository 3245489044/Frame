
using System;
using System.Collections.Generic;
using System.Linq;
using Modules.GraphProcessing.Algorithms;
using RefModels = UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Core
{
    /// <summary>
    /// 环检测。实现已下沉至 GraphProcessing.Algorithms.DefaultCycleDetector，
    /// 本类仅作 UR 领域模型的薄转发（消费方零改动）。
    /// </summary>
    public static class CircularDependencyDetector
    {
        private static readonly DefaultCycleDetector<string> _detector = new();

        public static RefModels.CircularDependencyResult Analyze(IEnumerable<string> nodeNames, Func<string, IEnumerable<string>> getDependencies)
        {
            var allNodes = nodeNames.ToList();
            var cycles = _detector.FindCycles(allNodes, getDependencies);

            var result = new RefModels.CircularDependencyResult
            {
                HasCircularDependencies = cycles.Count > 0
            };

            foreach (var cycle in cycles)
            {
                result.CircularDependencies = result.CircularDependencies.Append(
                    new RefModels.CircularDependency
                    {
                        ResourceChain = new List<string>(cycle),
                        ReferenceType = RefModels.ReferenceType.Dependency
                    });
            }

            return result;
        }
    }
}

