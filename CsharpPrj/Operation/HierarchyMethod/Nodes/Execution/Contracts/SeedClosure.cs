using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 种子闭合纯判据（生成期为主、运行期兜底）：首步 inport − (种子键 ∪ 上游产出键)。
/// 判据载体 = 键集合（结构事实），不含语义。
/// </summary>
public static class SeedClosure
{
    public static IReadOnlyList<string> Uncovered(
        IEnumerable<string> firstStepInports,
        IEnumerable<string> providedSeedKeys,
        IEnumerable<string>? upstreamProducedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(firstStepInports);
        ArgumentNullException.ThrowIfNull(providedSeedKeys);

        var covered = new HashSet<string>(
            providedSeedKeys.Where(static k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);
        if (upstreamProducedKeys is not null)
        {
            foreach (var key in upstreamProducedKeys)
                if (!string.IsNullOrWhiteSpace(key)) covered.Add(key);
        }

        return firstStepInports
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Where(k => !covered.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();
    }
}
