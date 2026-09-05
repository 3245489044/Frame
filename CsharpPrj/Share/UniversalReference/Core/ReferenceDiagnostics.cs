using System;
using System.Collections.Generic;
using Modules.GraphProcessing.Diagnostics;
using UniversalWorkflowFramework.Core.Diagnostics;

namespace UniversalWorkflowFramework.Core
{
    /// <summary>
    /// 引用诊断。实现已下沉至 GraphProcessing.Diagnostics.DefaultReferenceDiagnostics，
    /// 本类仅作 UR DiagnosticResult 的薄转发。
    /// </summary>
    public static class ReferenceDiagnostics
    {
        private static readonly DefaultReferenceDiagnostics _impl = new();

        public static DiagnosticResult RunFullDiagnostics(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            Func<HashSet<string>> getUsedReferences)
        {
            var gpResult = _impl.RunFullDiagnostics(allReferences, getDependencies, getUsedReferences);

            var result = new DiagnosticResult
            {
                TotalReferences = gpResult.TotalReferences,
                OrphanedReferences = gpResult.OrphanedCount,
                UnusedReferences = gpResult.UnusedCount,
                CircularReferences = gpResult.CircularReferenceCount
            };

            result.Errors.AddRange(gpResult.Errors);
            result.Warnings.AddRange(gpResult.Warnings);
            result.Info.AddRange(gpResult.Info);
            result.HasIssues = gpResult.HasIssues;

            return result;
        }
    }
}
