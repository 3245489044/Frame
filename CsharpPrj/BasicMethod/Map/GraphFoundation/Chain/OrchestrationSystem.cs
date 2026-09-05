
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Core;

namespace GraphFoundation.Chain
{
    public class OrchestrationSystem<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly List<IOrchestrationStep> _steps = new();

        public OrchestrationSystem(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public void AddStep(IOrchestrationStep step)
        {
            _steps.Add(step);
        }

        public OrchestrationResult Execute()
        {
            var result = new OrchestrationResult();
            foreach (var step in _steps)
            {
                try
                {
                    step.Execute(_graph);
                    result.SuccessfulSteps.Add(step.Name);
                }
                catch (Exception ex)
                {
                    result.FailedSteps.Add((step.Name, ex.Message));
                }
            }
            return result;
        }
    }

    public interface IOrchestrationStep
    {
        string Name { get; }
        void Execute(object graphContext);
    }



    public class OrchestrationResult
    {
        public List<string> SuccessfulSteps { get; } = new();
        public List<(string Name, string Error)> FailedSteps { get; } = new();
        public bool IsSuccess => FailedSteps.Count == 0;
    }

    public class ChainBuilder<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;

        public ChainBuilder(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public Chain<TKey> BuildChain(string name, TKey startKey)
        {
            var chain = new Chain<TKey>(name);
            var visited = new HashSet<TKey>();
            var queue = new Queue<TKey>();
            queue.Enqueue(startKey);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current)) continue;
                chain.Add(current);

                foreach (var edge in _graph.GetOutgoingEdges(current))
                {
                    if (!visited.Contains(edge.Target))
                        queue.Enqueue(edge.Target);
                }
            }

            return chain;
        }
    }
}
