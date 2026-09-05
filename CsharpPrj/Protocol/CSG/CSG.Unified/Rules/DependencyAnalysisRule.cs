using CSG.Unified;

namespace CSG.Unified.Rules;

public class DependencyAnalysisRule : IOptimizationRule
{
    public string Name => "DependencyAnalysis";

    public int Order => 0;

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();

        var stepIndexMap = new Dictionary<string, int>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            stepIndexMap[plan.Steps[i].StepName] = i;
        }

        var dependencyGraph = BuildDependencyGraph(plan, stepIndexMap);

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (step.ReferenceStepName != null)
            {
                step.Dependencies.Add(step.ReferenceStepName);
            }
        }

        var cycle = DetectCycle(dependencyGraph, plan.Steps);
        if (cycle != null)
        {
            plan.PreValidation.AddError(
                CsgErrorCode.CycleDetected,
                "DependencyCycle",
                $"检测到循环依赖: {string.Join(" → ", cycle)}");
            return plan;
        }

        var sortedIndices = TopologicalSort(dependencyGraph, plan.Steps.Count);

        plan.Steps = sortedIndices.Select(i => plan.Steps[i]).ToList();

        plan.ParallelGroups = Enumerable.Range(0, plan.Steps.Count)
            .Select(i => new HashSet<int> { i })
            .ToList();

        return plan;
    }

    private static List<int>[] BuildDependencyGraph(ExecutionPlan plan, Dictionary<string, int> stepIndexMap)
    {
        var n = plan.Steps.Count;
        var graph = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            graph[i] = new List<int>();
        }

        for (int i = 0; i < n; i++)
        {
            var step = plan.Steps[i];
            if (step.ReferenceStepName != null &&
                stepIndexMap.TryGetValue(step.ReferenceStepName, out var dependencyIndex))
            {
                graph[i].Add(dependencyIndex);
            }
        }

        return graph;
    }

    private static List<string>? DetectCycle(List<int>[] graph, List<PlanStep> steps)
    {
        var n = graph.Length;
        var color = new int[n];
        var parent = new int[n];
        Array.Fill(parent, -1);

        for (int i = 0; i < n; i++)
        {
            if (color[i] == 0)
            {
                var cycle = DfsVisit(i, graph, steps, color, parent);
                if (cycle != null)
                    return cycle;
            }
        }

        return null;
    }

    private static List<string>? DfsVisit(
        int node,
        List<int>[] graph,
        List<PlanStep> steps,
        int[] color,
        int[] parent)
    {
        color[node] = 1;

        foreach (var neighbor in graph[node])
        {
            if (color[neighbor] == 1)
            {
                var cycle = new List<string>();
                cycle.Add(steps[neighbor].StepName);
                var current = node;
                while (current != neighbor && current != -1)
                {
                    cycle.Add(steps[current].StepName);
                    current = parent[current];
                }
                cycle.Add(steps[neighbor].StepName);
                cycle.Reverse();
                return cycle;
            }

            if (color[neighbor] == 0)
            {
                parent[neighbor] = node;
                var result = DfsVisit(neighbor, graph, steps, color, parent);
                if (result != null)
                    return result;
            }
        }

        color[node] = 2;
        return null;
    }

    private static List<int> TopologicalSort(List<int>[] graph, int nodeCount)
    {
        var inDegree = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            foreach (var neighbor in graph[i])
            {
                inDegree[neighbor]++;
            }
        }

        var queue = new Queue<int>();
        for (int i = 0; i < nodeCount; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var result = new List<int>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            foreach (var neighbor in graph[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        return result;
    }
}
