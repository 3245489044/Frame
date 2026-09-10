
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using Modules.Pipeline;
using SkeletonFlow;

namespace GraphFramework.Pipeline
{
    public interface IGraphPipelineStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        string Name { get; }

        IGraph<TKey, TNodeData, TEdgeData> Execute(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Modules.Pipeline.IPipelineContext? context = null);
    }

    public class GraphPipeline<TKey, TNodeData, TEdgeData> : IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly List<IGraphPipelineStep<TKey, TNodeData, TEdgeData>> _steps = new();
        private readonly Modules.Pipeline.PipelineContext _context = new();
        private readonly string _name;
        private readonly string _description;

        public string Name => _name;

        public string Description => _description;

        public int StepCount => _steps.Count;

        public IReadOnlyList<IGraphPipelineStep<TKey, TNodeData, TEdgeData>> Steps => _steps.AsReadOnly();

        public Modules.Pipeline.IPipelineContext Context => _context;

        public GraphPipeline(string name = "default", string? description = null)
        {
            _name = name;
            _description = description ?? $"GraphPipeline<{typeof(TKey).Name}, {typeof(TNodeData).Name}, {typeof(TEdgeData).Name}>";
        }

        public GraphPipeline<TKey, TNodeData, TEdgeData> AddStep(IGraphPipelineStep<TKey, TNodeData, TEdgeData> step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            _steps.Add(step);
            return this;
        }

        public GraphPipeline<TKey, TNodeData, TEdgeData> AddSteps(params IGraphPipelineStep<TKey, TNodeData, TEdgeData>[] steps)
        {
            foreach (var step in steps)
                AddStep(step);
            return this;
        }

        public GraphPipeline<TKey, TNodeData, TEdgeData> InsertStep(int index, IGraphPipelineStep<TKey, TNodeData, TEdgeData> step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (index < 0 || index > _steps.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _steps.Insert(index, step);
            return this;
        }

        public bool RemoveStep(IGraphPipelineStep<TKey, TNodeData, TEdgeData> step)
        {
            return _steps.Remove(step);
        }

        public void ClearSteps()
        {
            _steps.Clear();
        }

        public IReadOnlyList<string> GetStepNames()
        {
            var names = new List<string>(_steps.Count);
            foreach (var step in _steps)
                names.Add(step.Name);
            return names.AsReadOnly();
        }


        IGraph<TKey, TNodeData, TEdgeData> ISkeleton<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>.Execute(
            IGraph<TKey, TNodeData, TEdgeData> input, SkeletonContext? context)
            => Execute(input);

        async Task<IGraph<TKey, TNodeData, TEdgeData>> ISkeleton<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>.ExecuteAsync(
            IGraph<TKey, TNodeData, TEdgeData> input, SkeletonContext? context)
            => await Task.Run(() => Execute(input)).ConfigureAwait(false);


        IGraph<TKey, TNodeData, TEdgeData> IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>.Execute(
            IGraph<TKey, TNodeData, TEdgeData> input, PipelineContext? context)
            => Execute(input);

        async Task<IGraph<TKey, TNodeData, TEdgeData>> IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>.ExecuteAsync(
            IGraph<TKey, TNodeData, TEdgeData> input, PipelineContext? context)
            => await Task.Run(() => Execute(input)).ConfigureAwait(false);


        public IGraph<TKey, TNodeData, TEdgeData> Execute(IGraph<TKey, TNodeData, TEdgeData> input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var current = input;
            foreach (var step in _steps)
            {
                current = step.Execute(current, _context);
            }

            return current;
        }


        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateBasic(string name = "default")
            => new(name);

        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateFull(string name = "full")
        {
            return new GraphPipeline<TKey, TNodeData, TEdgeData>(name);
        }
    }
}
