
#nullable enable

using System;
using System.Collections.Generic;
using GraphFoundation.Abstractions;
using Modules.Pipeline;
using TConfig;

namespace GraphFramework.Pipeline
{
    public class GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Modules.Pipeline.Pipeline _pipeline;

        public string Name { get; }

        public int StageCount => _pipeline.Count;

        public GraphPipelineOrchestrator(string name = "GraphOrchestrator")
        {
            Name = name;
            _pipeline = new Modules.Pipeline.Pipeline();
        }

        public GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData> RegisterStage(
            string name,
            int order,
            bool isFatal,
            Func<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> handler)
        {
            _pipeline.RegisterStage(new GraphProcessingStage<TKey, TNodeData, TEdgeData>(name, order, isFatal, handler));
            return this;
        }

        public GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData> RegisterStep(
            IGraphPipelineStep<TKey, TNodeData, TEdgeData> step,
            int order,
            bool isFatal = false)
        {
            _pipeline.RegisterStage(new GraphStepStage<TKey, TNodeData, TEdgeData>(step, order, isFatal));
            return this;
        }

        public GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData> RegisterPipeline(
            GraphPipeline<TKey, TNodeData, TEdgeData> pipeline,
            int order,
            bool isFatal = false)
        {
            _pipeline.RegisterStage(new GraphPipelineStage<TKey, TNodeData, TEdgeData>(pipeline, order, isFatal));
            return this;
        }

        public PipelineResult Execute(IGraph<TKey, TNodeData, TEdgeData> input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return _pipeline.Execute(input);
        }

        public void Clear()
        {
            _pipeline.Clear();
        }
    }

    internal class GraphProcessingStage<TKey, TNodeData, TEdgeData> : IPipelineStage
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Func<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> _handler;

        public string Name { get; }
        public int Order { get; }
        public bool IsFatal { get; }

        public GraphProcessingStage(
            string name,
            int order,
            bool isFatal,
            Func<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> handler)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Order = order;
            IsFatal = isFatal;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public object? Process(object input, ErrorCollector errors, PipelineContext context)
        {
            if (input is IGraph<TKey, TNodeData, TEdgeData> graph)
            {
                return _handler(graph);
            }
            errors.Add($"STAGE_{Name.ToUpper()}_TYPE_ERROR",
                $"Stage '{Name}' 期望输入类型为 IGraph<...>，实际为 {input?.GetType().Name ?? "null"}",
                IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
            return null;
        }
    }

    internal class GraphStepStage<TKey, TNodeData, TEdgeData> : IPipelineStage
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IGraphPipelineStep<TKey, TNodeData, TEdgeData> _step;

        public string Name { get; }
        public int Order { get; }
        public bool IsFatal { get; }

        public GraphStepStage(
            IGraphPipelineStep<TKey, TNodeData, TEdgeData> step,
            int order,
            bool isFatal)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            Name = step.Name;
            Order = order;
            IsFatal = isFatal;
        }

        public object? Process(object input, ErrorCollector errors, PipelineContext context)
        {
            if (input is IGraph<TKey, TNodeData, TEdgeData> graph)
            {
                return _step.Execute(graph, context);
            }
            errors.Add($"STAGE_{Name.ToUpper()}_TYPE_ERROR",
                $"Stage '{Name}' 期望输入类型为 IGraph<...>，实际为 {input?.GetType().Name ?? "null"}",
                IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
            return null;
        }
    }

    internal class GraphPipelineStage<TKey, TNodeData, TEdgeData> : IPipelineStage
        where TKey : notnull
        where TNodeData : class
    {
        private readonly GraphPipeline<TKey, TNodeData, TEdgeData> _pipeline;

        public string Name { get; }
        public int Order { get; }
        public bool IsFatal { get; }

        public GraphPipelineStage(
            GraphPipeline<TKey, TNodeData, TEdgeData> pipeline,
            int order,
            bool isFatal)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Name = pipeline.Name;
            Order = order;
            IsFatal = isFatal;
        }

        public object? Process(object input, ErrorCollector errors, PipelineContext context)
        {
            if (input is IGraph<TKey, TNodeData, TEdgeData> graph)
            {
                return _pipeline.Execute(graph);
            }
            errors.Add($"STAGE_{Name.ToUpper()}_TYPE_ERROR",
                $"Stage '{Name}' 期望输入类型为 IGraph<...>，实际为 {input?.GetType().Name ?? "null"}",
                IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
            return null;
        }
    }
}
