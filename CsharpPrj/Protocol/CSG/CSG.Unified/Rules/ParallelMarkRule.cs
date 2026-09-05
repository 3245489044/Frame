using CSG.Unified;

namespace CSG.Unified.Rules;

public class ParallelMarkRule : IOptimizationRule
{
    public string Name => "ParallelMark";

    public int Order => 1;

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();

        var levels = AssignLevels(plan);

        plan.ParallelGroups = levels;

        return plan;
    }

    private static List<HashSet<int>> AssignLevels(ExecutionPlan plan)
    {
        var n = plan.Steps.Count;
        if (n == 0) return new List<HashSet<int>>();

        var stepIndexMap = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            stepIndexMap[plan.Steps[i].StepName] = i;
        }

        var inDegree = new int[n];
        var outEdges = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            outEdges[i] = new List<int>();
        }

        for (int i = 0; i < n; i++)
        {
            var step = plan.Steps[i];
            foreach (var dep in step.Dependencies)
            {
                if (stepIndexMap.TryGetValue(dep, out var depIndex))
                {
                    inDegree[i]++;
                    outEdges[depIndex].Add(i);
                }
            }
        }

        var level = new int[n];
        Array.Fill(level, -1);

        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
            {
                level[i] = 0;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            foreach (var neighbor in outEdges[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    level[neighbor] = level[node] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        var maxLevel = level.Max();
        var groups = new List<HashSet<int>>();
        for (int l = 0; l <= maxLevel; l++)
        {
            var group = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (level[i] == l)
                {
                    group.Add(i);
                }
            }
            if (group.Count > 0)
            {
                groups.Add(group);
            }
        }

        if (groups.Count == 0)
        {
            for (int i = 0; i < n; i++)
            {
                groups.Add(new HashSet<int> { i });
            }
        }

        return groups;
    }
}
