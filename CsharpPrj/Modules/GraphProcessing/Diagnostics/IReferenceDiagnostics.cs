#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Modules.GraphProcessing.Algorithms;

namespace Modules.GraphProcessing.Diagnostics
{
    /// <summary>引用诊断结果（通用计数与消息）。</summary>
    public class ReferenceDiagnosticResult
    {
        public int TotalReferences { get; set; }

        public int CircularReferenceCount { get; set; }

        public int OrphanedCount { get; set; }

        public int UnusedCount { get; set; }

        public int MissingCount { get; set; }

        public List<string> Errors { get; } = new();

        public List<string> Warnings { get; } = new();

        public List<string> Info { get; } = new();

        public bool HasIssues => Errors.Count > 0 || Warnings.Count > 0;
    }

    /// <summary>引用诊断器（循环/孤立/未用/缺失，泛化自 UniversalReference.ReferenceDiagnostics）。</summary>
    public interface IReferenceDiagnostics
    {
        ReferenceDiagnosticResult RunFullDiagnostics(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            Func<HashSet<string>> getUsedReferences);
    }

    /// <summary>默认引用诊断实现。</summary>
    public class DefaultReferenceDiagnostics : IReferenceDiagnostics
    {
        private readonly ICycleDetector<string> _cycleDetector;

        public DefaultReferenceDiagnostics(ICycleDetector<string>? cycleDetector = null)
            => _cycleDetector = cycleDetector ?? new DefaultCycleDetector<string>();

        public ReferenceDiagnosticResult RunFullDiagnostics(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            Func<HashSet<string>> getUsedReferences)
        {
            if (allReferences == null) throw new ArgumentNullException(nameof(allReferences));
            if (getDependencies == null) throw new ArgumentNullException(nameof(getDependencies));
            if (getUsedReferences == null) throw new ArgumentNullException(nameof(getUsedReferences));

            var result = new ReferenceDiagnosticResult
            {
                TotalReferences = allReferences.Count
            };

            CheckCircularReferences(allReferences, getDependencies, result);
            CheckOrphanedReferences(allReferences, getDependencies, result);
            CheckUnusedReferences(allReferences, getUsedReferences, result);
            CheckMissingReferences(allReferences, getDependencies, result);

            return result;
        }

        private void CheckCircularReferences(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            ReferenceDiagnosticResult result)
        {
            var cycles = _cycleDetector.FindCycles(allReferences.Keys.ToList(), getDependencies);
            result.CircularReferenceCount = cycles.Count;
            foreach (var cycle in cycles)
            {
                result.Errors.Add($"引用 '{string.Join(" -> ", cycle)}' 存在循环引用");
            }
        }

        private void CheckOrphanedReferences(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            ReferenceDiagnosticResult result)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in allReferences)
            {
                var deps = getDependencies(kv.Key);
                if (deps != null)
                {
                    foreach (var dep in deps)
                        referenced.Add(dep);
                }
            }

            foreach (var kv in allReferences)
            {
                if (!referenced.Contains(kv.Key))
                {
                    result.OrphanedCount++;
                    result.Warnings.Add($"引用 '{kv.Key}' 可能未被使用（孤立引用）");
                }
            }
        }

        private void CheckUnusedReferences(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<HashSet<string>> getUsedReferences,
            ReferenceDiagnosticResult result)
        {
            var used = getUsedReferences();
            if (used == null) return;

            foreach (var kv in allReferences)
            {
                if (!used.Contains(kv.Key))
                {
                    result.UnusedCount++;
                    result.Info.Add($"引用 '{kv.Key}' 可能未被使用");
                }
            }
        }

        private void CheckMissingReferences(
            IDictionary<string, IEnumerable<string>> allReferences,
            Func<string, IEnumerable<string>> getDependencies,
            ReferenceDiagnosticResult result)
        {
            foreach (var kv in allReferences)
            {
                var deps = getDependencies(kv.Key);
                if (deps != null)
                {
                    foreach (var dep in deps)
                    {
                        if (!allReferences.ContainsKey(dep))
                        {
                            result.MissingCount++;
                            result.Errors.Add($"引用 '{kv.Key}' 引用了不存在的引用 '{dep}'");
                        }
                    }
                }
            }
        }
    }
}
