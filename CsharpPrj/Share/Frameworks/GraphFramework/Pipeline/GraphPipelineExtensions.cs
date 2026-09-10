
#nullable enable

using System;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using Modules.Pipeline;

namespace GraphFramework.Pipeline
{
    public static class GraphPipelineExtensions
    {
        public static IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            AsPipeline<TKey, TNodeData, TEdgeData>(
                this GraphPipeline<TKey, TNodeData, TEdgeData> pipeline)
            where TKey : notnull
            where TNodeData : class
            => pipeline;

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            ToDelegatePipeline<TKey, TNodeData, TEdgeData>(
                this GraphPipeline<TKey, TNodeData, TEdgeData> pipeline)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                pipeline.Name,
                sync: input => pipeline.Execute(input),
                async: async input => await Task.Run(() => pipeline.Execute(input)).ConfigureAwait(false),
                description: pipeline.Description);
        }

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            Then<TKey, TNodeData, TEdgeData>(
                this IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> first,
                IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> second)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                $"{first.Name} → {second.Name}",
                sync: input =>
                {
                    var intermediate = first.Execute(input);
                    return second.Execute(intermediate);
                },
                async: async input =>
                {
                    var intermediate = await first.ExecuteAsync(input).ConfigureAwait(false);
                    return await second.ExecuteAsync(intermediate).ConfigureAwait(false);
                },
                description: $"Composite: {first.Description} → {second.Description}");
        }

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, (IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>)>
            Fork<TKey, TNodeData, TEdgeData>(
                this IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> first,
                IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> second)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, (IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>)>(
                $"Fork({first.Name}, {second.Name})",
                sync: input =>
                {
                    var result1 = first.Execute(input);
                    var result2 = second.Execute(input);
                    return (result1, result2);
                },
                async: async input =>
                {
                    var task1 = first.ExecuteAsync(input);
                    var task2 = second.ExecuteAsync(input);
                    await Task.WhenAll(task1, task2).ConfigureAwait(false);
                    return (task1.Result, task2.Result);
                },
                description: $"Fork: {first.Description} || {second.Description}");
        }

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            Guard<TKey, TNodeData, TEdgeData>(
                this IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> pipeline,
                Func<IGraph<TKey, TNodeData, TEdgeData>, bool> predicate)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                $"Guard({pipeline.Name})",
                sync: input => predicate(input) ? pipeline.Execute(input) : input,
                async: async input => predicate(input) ? await pipeline.ExecuteAsync(input).ConfigureAwait(false) : input,
                description: $"Conditional: {pipeline.Description}");
        }

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            Repeat<TKey, TNodeData, TEdgeData>(
                this IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> pipeline,
                int count)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                $"Repeat({pipeline.Name}, {count})",
                sync: input =>
                {
                    var current = input;
                    for (int i = 0; i < count; i++)
                        current = pipeline.Execute(current);
                    return current;
                },
                async: async input =>
                {
                    var current = input;
                    for (int i = 0; i < count; i++)
                        current = await pipeline.ExecuteAsync(current).ConfigureAwait(false);
                    return current;
                },
                description: $"Repeat {count}x: {pipeline.Description}");
        }

        public static DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            Tap<TKey, TNodeData, TEdgeData>(
                this IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>> pipeline,
                Action<IGraph<TKey, TNodeData, TEdgeData>> sideEffect)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                $"Tap({pipeline.Name})",
                sync: input =>
                {
                    sideEffect(input);
                    return pipeline.Execute(input);
                },
                async: async input =>
                {
                    sideEffect(input);
                    return await pipeline.ExecuteAsync(input).ConfigureAwait(false);
                },
                description: $"Tap: {pipeline.Description}");
        }

        public static IPipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>
            ToPipeline<TKey, TNodeData, TEdgeData>(
                this IGraphPipelineStep<TKey, TNodeData, TEdgeData> step)
            where TKey : notnull
            where TNodeData : class
        {
            return new DelegatePipeline<IGraph<TKey, TNodeData, TEdgeData>, IGraph<TKey, TNodeData, TEdgeData>>(
                step.Name,
                sync: input => step.Execute(input),
                async: async input => await Task.Run(() => step.Execute(input)).ConfigureAwait(false),
                description: $"Step: {step.Name}");
        }
    }
}
