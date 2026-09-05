using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class ClusterAnalyzer
{
    public static List<CodeCluster> ClusterElements(List<EnhancedCodeElement> elements, List<ElementDistance> distances, AnalysisConfiguration config)
    {
        if (elements == null || elements.Count == 0) return new List<CodeCluster>();
        if (elements.Count <= 3) return CreateSingleCluster(elements);

        return ClusterUsingTraditionalAlgorithm(elements, distances, config);
    }

    public static List<CodeCluster> CreateSingleCluster(List<EnhancedCodeElement> elements)
    {
        var cluster = new CodeCluster(0) { Name = "AllElements" };
        foreach (var element in elements)
            cluster.AddElement(element);
        cluster.Cohesion = 1.0;
        cluster.Coupling = 0.0;
        return new List<CodeCluster> { cluster };
    }

    private static List<CodeCluster> ClusterUsingTraditionalAlgorithm(List<EnhancedCodeElement> elements, List<ElementDistance> distances, AnalysisConfiguration config)
    {
        var clusters = new List<CodeCluster>();
        var assigned = new HashSet<string>();
        int clusterId = 0;

        var sortedDistances = distances
            .Where(d => d.Similarity > 0.3)
            .OrderByDescending(d => d.Similarity)
            .ToList();

        foreach (var dist in sortedDistances)
        {
            if (assigned.Contains(dist.SourceId) && assigned.Contains(dist.TargetId))
                continue;

            var source = elements.FirstOrDefault(e => e.Id == dist.SourceId);
            var target = elements.FirstOrDefault(e => e.Id == dist.TargetId);
            if (source == null || target == null) continue;

            var sourceCluster = clusters.FirstOrDefault(c => c.ContainsElement(dist.SourceId));
            var targetCluster = clusters.FirstOrDefault(c => c.ContainsElement(dist.TargetId));

            if (sourceCluster == null && targetCluster == null)
            {
                var cluster = new CodeCluster(clusterId++);
                cluster.AddElement(source);
                cluster.AddElement(target);
                assigned.Add(dist.SourceId);
                assigned.Add(dist.TargetId);
                clusters.Add(cluster);
            }
            else if (sourceCluster != null && targetCluster == null)
            {
                sourceCluster.AddElement(target);
                assigned.Add(dist.TargetId);
            }
            else if (sourceCluster == null && targetCluster != null)
            {
                targetCluster.AddElement(source);
                assigned.Add(dist.SourceId);
            }
        }

        foreach (var element in elements)
        {
            if (!assigned.Contains(element.Id))
            {
                var cluster = new CodeCluster(clusterId++);
                cluster.AddElement(element);
                assigned.Add(element.Id);
                clusters.Add(cluster);
            }
        }

        foreach (var cluster in clusters)
        {
            CalculateClusterMetrics(cluster, elements, distances);
        }

        return clusters;
    }

    private static void CalculateClusterMetrics(CodeCluster cluster, List<EnhancedCodeElement> allElements, List<ElementDistance> distances)
    {
        if (cluster.Elements.Count <= 1)
        {
            cluster.Cohesion = 1.0;
            cluster.Coupling = 0.0;
            return;
        }

        var internalDistances = distances.Where(d =>
            cluster.ContainsElement(d.SourceId) && cluster.ContainsElement(d.TargetId)).ToList();
        cluster.Cohesion = internalDistances.Any()
            ? internalDistances.Average(d => d.Similarity)
            : 0.5;

        var externalDistances = distances.Where(d =>
            (cluster.ContainsElement(d.SourceId) && !cluster.ContainsElement(d.TargetId)) ||
            (!cluster.ContainsElement(d.SourceId) && cluster.ContainsElement(d.TargetId))).ToList();
        cluster.Coupling = externalDistances.Any()
            ? externalDistances.Average(d => d.Similarity)
            : 0.0;
    }
}
