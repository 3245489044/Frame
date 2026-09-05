
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;
using Modules.Transformers;

namespace Modules.Pipeline
{

    public class ForEachPipeline<TContext, TItem> : PipelineBase<TContext, TContext>
    {
        private readonly Func<TContext, IReadOnlyList<TItem>> _itemsSelector;
        private readonly IPipeline<TContext, TContext> _itemPipeline;
        private readonly Action<TContext, TItem, int>? _onItemBefore;
        private readonly Action<TContext, TItem, int, bool>? _onItemAfter;
        private readonly int? _maxCount;
        private readonly bool _breakOnError;

        public ForEachPipeline(
            Func<TContext, IReadOnlyList<TItem>> itemsSelector,
            IPipeline<TContext, TContext> itemPipeline,
            Action<TContext, TItem, int>? onItemBefore = null,
            Action<TContext, TItem, int, bool>? onItemAfter = null,
            int? maxCount = null,
            bool breakOnError = false,
            string? name = null,
            string? description = null)
            : base(name ?? $"ForEach<{typeof(TItem).Name}>", description ?? "集合遍历管线")
        {
            _itemsSelector = itemsSelector ?? throw new ArgumentNullException(nameof(itemsSelector));
            _itemPipeline = itemPipeline ?? throw new ArgumentNullException(nameof(itemPipeline));
            _onItemBefore = onItemBefore;
            _onItemAfter = onItemAfter;
            _maxCount = maxCount;
            _breakOnError = breakOnError;
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            var items = _itemsSelector(input);
            int actualCount = _maxCount.HasValue ? Math.Min(items.Count, _maxCount.Value) : items.Count;

            for (int i = 0; i < actualCount; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var item = items[i];
                _onItemBefore?.Invoke(input, item, i);

                try
                {
                    _itemPipeline.Execute(input, context);
                    _onItemAfter?.Invoke(input, item, i, true);
                }
                catch (Exception)
                {
                    _onItemAfter?.Invoke(input, item, i, false);
                    if (_breakOnError)
                        throw;
                }
            }

            return input;
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            var items = _itemsSelector(input);
            int actualCount = _maxCount.HasValue ? Math.Min(items.Count, _maxCount.Value) : items.Count;

            for (int i = 0; i < actualCount; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var item = items[i];
                _onItemBefore?.Invoke(input, item, i);

                try
                {
                    await _itemPipeline.ExecuteAsync(input, context).ConfigureAwait(false);
                    _onItemAfter?.Invoke(input, item, i, true);
                }
                catch (Exception)
                {
                    _onItemAfter?.Invoke(input, item, i, false);
                    if (_breakOnError)
                        throw;
                }
            }

            return input;
        }
    }


    public class ParallelPipeline<TContext> : PipelineBase<TContext, TContext>
    {
        private readonly IReadOnlyList<IPipeline<TContext, TContext>> _pipelines;
        private readonly int _maxDegreeOfParallelism;
        private readonly bool _continueOnError;

        public ParallelPipeline(
            IReadOnlyList<IPipeline<TContext, TContext>> pipelines,
            int maxDegreeOfParallelism = 0,
            bool continueOnError = true,
            string? name = null,
            string? description = null)
            : base(name ?? $"Parallel({pipelines.Count})", description ?? "并行执行管线")
        {
            _pipelines = pipelines ?? throw new ArgumentNullException(nameof(pipelines));
            _maxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : 0;
            _continueOnError = continueOnError;
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            foreach (var pipeline in _pipelines)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    pipeline.Execute(input, context);
                }
                catch (Exception)
                {
                    if (!_continueOnError)
                        throw;
                }
            }
            return input;
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            if (_pipelines.Count == 0)
                return input;

            using var semaphore = _maxDegreeOfParallelism > 0
                ? new SemaphoreSlim(_maxDegreeOfParallelism)
                : null;

            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            foreach (var pipeline in _pipelines)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (semaphore != null)
                    await semaphore.WaitAsync(context.CancellationToken).ConfigureAwait(false);

                tasks.Add(ExecuteOneAsync(pipeline, input, context, semaphore, exceptions));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (exceptions.Count > 0 && !_continueOnError)
            {
                throw new AggregateException(
                    $"并行执行中 {exceptions.Count} 个子管线失败",
                    exceptions);
            }

            return input;
        }

        private async Task ExecuteOneAsync(
            IPipeline<TContext, TContext> pipeline,
            TContext input,
            PipelineContext context,
            SemaphoreSlim? semaphore,
            List<Exception> exceptions)
        {
            try
            {
                await pipeline.ExecuteAsync(input, context).ConfigureAwait(false);
            }
            catch (Exception ex) when (_continueOnError)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
            finally
            {
                semaphore?.Release();
            }
        }
    }


    public class PipelineSwitch<TContext, TKey> : PipelineBase<TContext, TContext>
        where TKey : notnull
    {
        private readonly Func<TContext, TKey> _keySelector;
        private readonly Dictionary<TKey, IPipeline<TContext, TContext>> _branches;
        private readonly IPipeline<TContext, TContext>? _defaultBranch;

        public PipelineSwitch(
            Func<TContext, TKey> keySelector,
            Dictionary<TKey, IPipeline<TContext, TContext>> branches,
            IPipeline<TContext, TContext>? defaultBranch = null,
            string? name = null,
            string? description = null)
            : base(name ?? $"Switch<{typeof(TKey).Name}>", description ?? "多路分支管线")
        {
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            _branches = branches ?? throw new ArgumentNullException(nameof(branches));
            _defaultBranch = defaultBranch;
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            var key = _keySelector(input);

            if (_branches.TryGetValue(key, out var branch))
                return branch.Execute(input, context);

            if (_defaultBranch != null)
                return _defaultBranch.Execute(input, context);

            throw new KeyNotFoundException($"PipelineSwitch: 未找到键 '{key}' 对应的分支，且未提供默认分支。");
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            var key = _keySelector(input);

            if (_branches.TryGetValue(key, out var branch))
                return await branch.ExecuteAsync(input, context).ConfigureAwait(false);

            if (_defaultBranch != null)
                return await _defaultBranch.ExecuteAsync(input, context).ConfigureAwait(false);

            throw new KeyNotFoundException($"PipelineSwitch: 未找到键 '{key}' 对应的分支，且未提供默认分支。");
        }
    }


    public class PipelineBranch<TContext> : PipelineBase<TContext, TContext>
    {
        private readonly Func<TContext, bool> _predicate;
        private readonly IPipeline<TContext, TContext> _trueBranch;
        private readonly IPipeline<TContext, TContext> _falseBranch;

        public PipelineBranch(
            Func<TContext, bool> predicate,
            IPipeline<TContext, TContext> trueBranch,
            IPipeline<TContext, TContext> falseBranch,
            string? name = null,
            string? description = null)
            : base(name ?? "Branch", description ?? "双路条件分支管线")
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _trueBranch = trueBranch ?? throw new ArgumentNullException(nameof(trueBranch));
            _falseBranch = falseBranch ?? throw new ArgumentNullException(nameof(falseBranch));
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            return _predicate(input)
                ? _trueBranch.Execute(input, context)
                : _falseBranch.Execute(input, context);
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            return _predicate(input)
                ? await _trueBranch.ExecuteAsync(input, context).ConfigureAwait(false)
                : await _falseBranch.ExecuteAsync(input, context).ConfigureAwait(false);
        }
    }


    public class PipelineTryCatch<TContext> : PipelineBase<TContext, TContext>
    {
        private readonly IPipeline<TContext, TContext> _mainPipeline;
        private readonly IPipeline<TContext, TContext>? _fallbackPipeline;
        private readonly Func<Exception, bool>? _exceptionFilter;
        private readonly Action<Exception>? _onError;

        public PipelineTryCatch(
            IPipeline<TContext, TContext> mainPipeline,
            IPipeline<TContext, TContext>? fallbackPipeline = null,
            Func<Exception, bool>? exceptionFilter = null,
            Action<Exception>? onError = null,
            string? name = null,
            string? description = null)
            : base(name ?? $"Try({mainPipeline.Name})", description ?? "异常处理/降级管线")
        {
            _mainPipeline = mainPipeline ?? throw new ArgumentNullException(nameof(mainPipeline));
            _fallbackPipeline = fallbackPipeline;
            _exceptionFilter = exceptionFilter;
            _onError = onError;
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            try
            {
                return _mainPipeline.Execute(input, context);
            }
            catch (Exception ex) when (_exceptionFilter?.Invoke(ex) ?? IsRecoverable(ex))
            {
                _onError?.Invoke(ex);

                if (_fallbackPipeline != null)
                {
                    try
                    {
                        return _fallbackPipeline.Execute(input, context);
                    }
                    catch (Exception fallbackEx)
                    {
                        throw new AggregateException(
                            $"主路径异常: {ex.Message}, 降级路径也失败: {fallbackEx.Message}",
                            ex, fallbackEx);
                    }
                }

                throw;
            }
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            try
            {
                return await _mainPipeline.ExecuteAsync(input, context).ConfigureAwait(false);
            }
            catch (Exception ex) when (_exceptionFilter?.Invoke(ex) ?? IsRecoverable(ex))
            {
                _onError?.Invoke(ex);

                if (_fallbackPipeline != null)
                {
                    try
                    {
                        return await _fallbackPipeline.ExecuteAsync(input, context).ConfigureAwait(false);
                    }
                    catch (Exception fallbackEx)
                    {
                        throw new AggregateException(
                            $"主路径异常: {ex.Message}, 降级路径也失败: {fallbackEx.Message}",
                            ex, fallbackEx);
                    }
                }

                throw;
            }
        }

        private static bool IsRecoverable(Exception ex)
        {
            return ex is not (StackOverflowException
                           or OutOfMemoryException
                           or AccessViolationException
                           or InvalidProgramException);
        }
    }


    public class PipelineRetry<TContext> : PipelineBase<TContext, TContext>
    {
        private readonly IPipeline<TContext, TContext> _innerPipeline;
        private readonly int _maxRetries;
        private readonly int _delayMs;
        private readonly Func<Exception, int, bool>? _shouldRetry;
        private readonly Action<Exception, int>? _onRetry;

        public PipelineRetry(
            IPipeline<TContext, TContext> innerPipeline,
            int maxRetries = 3,
            int delayMs = 100,
            Func<Exception, int, bool>? shouldRetry = null,
            Action<Exception, int>? onRetry = null,
            string? name = null,
            string? description = null)
            : base(name ?? $"Retry({maxRetries})", description ?? "重试管线")
        {
            _innerPipeline = innerPipeline ?? throw new ArgumentNullException(nameof(innerPipeline));
            _maxRetries = maxRetries > 0 ? maxRetries : 3;
            _delayMs = delayMs;
            _shouldRetry = shouldRetry;
            _onRetry = onRetry;
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return _innerPipeline.Execute(input, context);
                }
                catch (Exception ex)
                {
                    if (attempt >= _maxRetries - 1)
                        throw new InvalidOperationException(
                            $"PipelineRetry: 重试 {_maxRetries} 次后仍然失败。", ex);

                    if (_shouldRetry != null && !_shouldRetry(ex, attempt))
                        throw;

                    _onRetry?.Invoke(ex, attempt);

                    if (_delayMs > 0)
                        System.Threading.Thread.Sleep(_delayMs);

                    attempt++;
                }
            }
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await _innerPipeline.ExecuteAsync(input, context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (attempt >= _maxRetries - 1)
                        throw new InvalidOperationException(
                            $"PipelineRetry: 重试 {_maxRetries} 次后仍然失败。", ex);

                    if (_shouldRetry != null && !_shouldRetry(ex, attempt))
                        throw;

                    _onRetry?.Invoke(ex, attempt);

                    if (_delayMs > 0)
                        await Task.Delay(_delayMs, context.CancellationToken).ConfigureAwait(false);

                    attempt++;
                }
            }
        }
    }


    public class ConditionalPipeline<TContext> : PipelineBase<TContext, TContext>
    {
        private readonly IPipeline<TContext, TContext> _inner;
        private readonly Func<TContext, bool> _condition;

        public ConditionalPipeline(
            IPipeline<TContext, TContext> inner,
            Func<TContext, bool> condition,
            string? name = null,
            string? description = null)
            : base(name ?? $"Conditional({inner.Name})", description ?? $"条件执行包装器 for {inner.Name}")
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            if (!_condition(input))
                return input;

            return _inner.Execute(input, context);
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            if (!_condition(input))
                return input;

            return await _inner.ExecuteAsync(input, context).ConfigureAwait(false);
        }
    }


    public class PipelineAdapter<TContext, TIn, TOut> : PipelineBase<TContext, TContext>
    {
        private readonly IPipeline<TIn, TOut> _inner;
        private readonly Func<TContext, TIn> _inputSelector;
        private readonly Action<TContext, TOut> _outputWriter;

        public PipelineAdapter(
            IPipeline<TIn, TOut> inner,
            Func<TContext, TIn> inputSelector,
            Action<TContext, TOut> outputWriter,
            string? name = null,
            string? description = null)
            : base(name ?? $"Adapter({inner.Name})", description ?? $"Adapter for {inner.Name}")
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _inputSelector = inputSelector ?? throw new ArgumentNullException(nameof(inputSelector));
            _outputWriter = outputWriter ?? throw new ArgumentNullException(nameof(outputWriter));
        }

        protected override TContext ExecuteCore(TContext input, PipelineContext context)
        {
            var innerInput = _inputSelector(input);
            var innerOutput = _inner.Execute(innerInput, context);
            _outputWriter(input, innerOutput);
            return input;
        }

        protected override async Task<TContext> ExecuteCoreAsync(TContext input, PipelineContext context)
        {
            var innerInput = _inputSelector(input);
            var innerOutput = await _inner.ExecuteAsync(innerInput, context).ConfigureAwait(false);
            _outputWriter(input, innerOutput);
            return input;
        }
    }


    public static class CombinatorsExtensions
    {
        public static ConditionalPipeline<TContext> When<TContext>(
            this IPipeline<TContext, TContext> pipeline,
            Func<TContext, bool> condition)
        {
            return new ConditionalPipeline<TContext>(pipeline, condition);
        }

        public static ConditionalPipeline<TContext> SkipWhen<TContext>(
            this IPipeline<TContext, TContext> pipeline,
            Func<TContext, bool> condition)
        {
            return new ConditionalPipeline<TContext>(pipeline, ctx => !condition(ctx));
        }

        public static ForEachPipeline<TContext, TItem> ForEach<TContext, TItem>(
            this IPipeline<TContext, TContext> itemPipeline,
            Func<TContext, IReadOnlyList<TItem>> itemsSelector,
            Action<TContext, TItem, int>? onItemBefore = null,
            Action<TContext, TItem, int, bool>? onItemAfter = null,
            int? maxCount = null,
            bool breakOnError = false,
            string? name = null)
        {
            return new ForEachPipeline<TContext, TItem>(
                itemsSelector, itemPipeline, onItemBefore, onItemAfter,
                maxCount, breakOnError, name);
        }

        public static ParallelPipeline<TContext> ParallelAll<TContext>(
            this IEnumerable<IPipeline<TContext, TContext>> pipelines,
            int maxDegreeOfParallelism = 0,
            bool continueOnError = true,
            string? name = null)
        {
            return new ParallelPipeline<TContext>(
                pipelines.ToList().AsReadOnly(),
                maxDegreeOfParallelism,
                continueOnError,
                name);
        }

        public static PipelineTryCatch<TContext> WithFallback<TContext>(
            this IPipeline<TContext, TContext> mainPipeline,
            IPipeline<TContext, TContext>? fallbackPipeline = null,
            Func<Exception, bool>? exceptionFilter = null,
            Action<Exception>? onError = null,
            string? name = null)
        {
            return new PipelineTryCatch<TContext>(
                mainPipeline, fallbackPipeline, exceptionFilter, onError, name);
        }

        public static PipelineRetry<TContext> WithRetryPolicy<TContext>(
            this IPipeline<TContext, TContext> innerPipeline,
            int maxRetries = 3,
            int delayMs = 100,
            Func<Exception, int, bool>? shouldRetry = null,
            Action<Exception, int>? onRetry = null,
            string? name = null)
        {
            return new PipelineRetry<TContext>(
                innerPipeline, maxRetries, delayMs, shouldRetry, onRetry, name);
        }

        public static PipelineAdapter<TContext, TIn, TOut> Adapt<TContext, TIn, TOut>(
            this IPipeline<TIn, TOut> inner,
            Func<TContext, TIn> inputSelector,
            Action<TContext, TOut> outputWriter,
            string? name = null)
        {
            return new PipelineAdapter<TContext, TIn, TOut>(
                inner, inputSelector, outputWriter, name);
        }


        public static ConditionalPipeline<T> FromConditionalTransformer<T>(
            this ITransformer<T, T> transformer,
            Func<T, bool> condition,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            ArgumentNullException.ThrowIfNull(condition);
            var pipeline = new TransformerToPipelineAdapter<T, T>(transformer, name ?? $"Conditional({transformer.Name})");
            return new ConditionalPipeline<T>(pipeline, condition, name);
        }

        public static PipelineTryCatch<T> FromTryTransformer<T>(
            this ITransformer<T, T> transformer,
            ITransformer<T, T>? fallbackTransformer = null,
            Func<Exception, bool>? exceptionFilter = null,
            Action<Exception>? onError = null,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            var mainPipeline = new TransformerToPipelineAdapter<T, T>(transformer, name ?? $"Try({transformer.Name})");
            var fallbackPipeline = fallbackTransformer != null
                ? new TransformerToPipelineAdapter<T, T>(fallbackTransformer)
                : null;
            return new PipelineTryCatch<T>(mainPipeline, fallbackPipeline, exceptionFilter, onError, name);
        }

        public static PipelineRetry<T> FromRetryTransformer<T>(
            this ITransformer<T, T> transformer,
            int maxRetries = 3,
            int delayMs = 100,
            Func<Exception, int, bool>? shouldRetry = null,
            Action<Exception, int>? onRetry = null,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            var pipeline = new TransformerToPipelineAdapter<T, T>(transformer, name ?? $"Retry({transformer.Name})");
            return new PipelineRetry<T>(pipeline, maxRetries, delayMs, shouldRetry, onRetry, name);
        }
    }
}
