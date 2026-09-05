
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Pipeline
{
    using Modules.Transformers;


    public class TransformerToPipelineAdapter<TIn, TOut> : PipelineBase<TIn, TOut>
    {
        private readonly ITransformer<TIn, TOut> _transformer;

        public TransformerToPipelineAdapter(ITransformer<TIn, TOut> transformer, string? name = null)
            : base(name ?? transformer.Name)
        {
            _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        }

        protected override TOut ExecuteCore(TIn input, PipelineContext context)
            => _transformer.Transform(input);

        protected override Task<TOut> ExecuteCoreAsync(TIn input, PipelineContext context)
            => Task.FromResult(_transformer.Transform(input));
    }

    public class AsyncToPipelineAdapter<TIn, TOut> : PipelineBase<TIn, TOut>
    {
        private readonly IAsyncTransformer<TIn, TOut> _transformer;

        public AsyncToPipelineAdapter(IAsyncTransformer<TIn, TOut> transformer, string? name = null)
            : base(name ?? transformer.Name)
        {
            _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        }

        protected override TOut ExecuteCore(TIn input, PipelineContext context)
            => _transformer.TransformAsync(input, context.CancellationToken).GetAwaiter().GetResult();

        protected override Task<TOut> ExecuteCoreAsync(TIn input, PipelineContext context)
            => _transformer.TransformAsync(input, context.CancellationToken);
    }


    public class PipelineToTransformerAdapter<TIn, TOut> : ITransformer<TIn, TOut>
    {
        private readonly IPipeline<TIn, TOut> _pipeline;

        public string Name => _pipeline.Name;

        public PipelineToTransformerAdapter(IPipeline<TIn, TOut> pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public TOut Transform(TIn input)
            => _pipeline.Execute(input);
    }

    public class PipelineToAsyncTransformerAdapter<TIn, TOut> : IAsyncTransformer<TIn, TOut>
    {
        private readonly IPipeline<TIn, TOut> _pipeline;

        public string Name => _pipeline.Name;

        public PipelineToAsyncTransformerAdapter(IPipeline<TIn, TOut> pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public Task<TOut> TransformAsync(TIn input, CancellationToken cancellationToken = default)
            => _pipeline.ExecuteAsync(input, new PipelineContext(cancellationToken));
    }


    public static class TransformerPipelineExtensions
    {
        public static IPipeline<TIn, TOut> ToPipeline<TIn, TOut>(
            this ITransformer<TIn, TOut> transformer,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            return new TransformerToPipelineAdapter<TIn, TOut>(transformer, name);
        }

        public static IPipeline<TIn, TOut> ToPipeline<TIn, TOut>(
            this IAsyncTransformer<TIn, TOut> transformer,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            return new AsyncToPipelineAdapter<TIn, TOut>(transformer, name);
        }

        public static ITransformer<TIn, TOut> ToTransformer<TIn, TOut>(
            this IPipeline<TIn, TOut> pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            return new PipelineToTransformerAdapter<TIn, TOut>(pipeline);
        }

        public static IAsyncTransformer<TIn, TOut> ToAsyncTransformer<TIn, TOut>(
            this IPipeline<TIn, TOut> pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            return new PipelineToAsyncTransformerAdapter<TIn, TOut>(pipeline);
        }
    }
}