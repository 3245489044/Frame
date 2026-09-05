
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;
using Modules.Transformers;
using TConfig;

namespace Modules.Pipeline
{

    public static class PipelineCombinators
    {
        public static IPipeline<TInput, TOutput> WithContext<TInput, TOutput>(
            this IPipeline<(TInput, PipelineContext), TOutput> pipeline)
        {
            return new DelegatePipeline<TInput, TOutput>(
                pipeline.Name,
                syncWithCtx: (input, ctx) => pipeline.Execute((input, ctx)),
                asyncWithCtx: async (input, ctx) => await pipeline.ExecuteAsync((input, ctx)).ConfigureAwait(false));
        }

        public static IPipeline<T, T> WithStages<T>(
            this IPipeline<T, T> pipeline,
            params IPipeline<T, T>[] stages)
        {
            if (stages == null || stages.Length == 0)
                return pipeline;

            return new DelegatePipeline<T, T>(
                $"{pipeline.Name}+Stages({stages.Length})",
                syncWithCtx: (input, ctx) =>
                {
                    var current = pipeline.Execute(input, ctx);
                    foreach (var stage in stages)
                        current = stage.Execute(current, ctx);
                    return current;
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    var current = await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false);
                    foreach (var stage in stages)
                        current = await stage.ExecuteAsync(current, ctx).ConfigureAwait(false);
                    return current;
                });
        }

        public static IPipeline<T, T> WithLifecycle<T>(
            this IPipeline<T, T> pipeline,
            Action<T>? onBefore = null,
            Action<T>? onAfter = null,
            Action<Exception>? onError = null)
        {
            return new DelegatePipeline<T, T>(
                pipeline.Name,
                syncWithCtx: (input, ctx) =>
                {
                    onBefore?.Invoke(input);
                    try
                    {
                        var result = pipeline.Execute(input, ctx);
                        onAfter?.Invoke(result);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                        throw;
                    }
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    onBefore?.Invoke(input);
                    try
                    {
                        var result = await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false);
                        onAfter?.Invoke(result);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                        throw;
                    }
                });
        }

        public static IPipeline<TInput, TOutput> WithErrorHandling<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            ErrorCollector errorCollector)
        {
            return pipeline.TryCatch(
                onError: ex => errorCollector.Add("PIPELINE_ERROR", ex.Message, ErrorLevel.Fatal, ex));
        }

        public static IPipeline<T, T> WithStagesAndErrors<T>(
            this IPipeline<T, T> pipeline,
            ErrorCollector errorCollector,
            params IPipeline<T, T>[] stages)
        {
            return pipeline.WithErrorHandling(errorCollector).WithStages(stages);
        }
    }


    internal static class ParallelHelper
    {
        public static T[] ExecuteInParallel<T>(int count, Func<int, T> action, CancellationToken cancellationToken)
        {
            var results = new T[count];
            var exceptions = new ConcurrentQueue<Exception>();

            System.Threading.Tasks.Parallel.For(0, count,
                new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                try { results[i] = action(i); }
                catch (Exception ex) { exceptions.Enqueue(ex); }
            });

            if (!exceptions.IsEmpty)
                throw new AggregateException(
                    $"{exceptions.Count} of {count} parallel executions failed",
                    exceptions);

            return results;
        }

        public static async Task<T[]> ExecuteInParallelAsync<T>(IEnumerable<Func<Task<T>>> taskFactories)
        {
            var tasks = taskFactories.Select(f => f()).ToArray();
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    public static class PipelineExtensions
    {

        public static IPipeline<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
            this IPipeline<TInput, TIntermediate> first,
            IPipeline<TIntermediate, TOutput> next)
        {
            var result = ((ISkeleton<TInput, TIntermediate>)first).Then((ISkeleton<TIntermediate, TOutput>)next);
            return new DelegatePipeline<TInput, TOutput>($"{first.Name}+{next.Name}",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }


        public static IPipeline<TInput, TOutput> Branch<TInput, TOutput>(
            this IPipeline<TInput, bool> condition,
            IPipeline<TInput, TOutput> trueBranch,
            IPipeline<TInput, TOutput> falseBranch)
        {
            var result = ((ISkeleton<TInput, bool>)condition).Branch(
                (ISkeleton<TInput, TOutput>)trueBranch,
                (ISkeleton<TInput, TOutput>)falseBranch);
            return new DelegatePipeline<TInput, TOutput>($"Branch({condition.Name})",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }

        public static IPipeline<TInput, TOutput> Branch<TInput, TOutput>(
            this Func<TInput, bool> predicate,
            IPipeline<TInput, TOutput> trueBranch,
            IPipeline<TInput, TOutput> falseBranch)
        {
            var result = predicate.Branch(
                (ISkeleton<TInput, TOutput>)trueBranch,
                (ISkeleton<TInput, TOutput>)falseBranch);
            return new DelegatePipeline<TInput, TOutput>($"Branch(Cond)",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }


        public static IPipeline<TInput, TInput> Loop<TInput>(
            this IPipeline<TInput, bool> condition,
            IPipeline<TInput, TInput> body)
        {
            var result = ((ISkeleton<TInput, bool>)condition).Loop((ISkeleton<TInput, TInput>)body);
            return new DelegatePipeline<TInput, TInput>($"Loop({condition.Name})",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }

        public static IPipeline<TInput, TInput> Loop<TInput>(
            this Func<TInput, bool> predicate,
            IPipeline<TInput, TInput> body)
        {
            var result = predicate.Loop((ISkeleton<TInput, TInput>)body);
            return new DelegatePipeline<TInput, TInput>($"Loop(Cond)",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }


        public static IPipeline<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
            this IPipeline<TInput, TOutput1> first,
            IPipeline<TInput, TOutput2> second)
        {
            var result = ((ISkeleton<TInput, TOutput1>)first).Parallel((ISkeleton<TInput, TOutput2>)second);
            return new DelegatePipeline<TInput, (TOutput1, TOutput2)>(
                $"Parallel({first.Name},{second.Name})",
                syncWithCtx: (i, ctx) => result.Execute(i, ctx),
                asyncWithCtx: async (i, ctx) => await result.ExecuteAsync(i, ctx).ConfigureAwait(false));
        }

        public static IPipeline<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
            this IPipeline<TInput, TOutput> element)
        {
            var result = ((ISkeleton<TInput, TOutput>)element).ForEach();
            return new DelegatePipeline<IEnumerable<TInput>, IEnumerable<TOutput>>(
                $"ForEach({element.Name})",
                syncWithCtx: (items, ctx) => result.Execute(items, ctx),
                asyncWithCtx: async (items, ctx) => await result.ExecuteAsync(items, ctx).ConfigureAwait(false));
        }


        public static IPipeline<TInput, TOutput> Compose<TInput, TOutput>(
            params IPipeline<object, object>[] steps)
        {
            if (steps == null || steps.Length == 0)
                throw new ArgumentException("At least one step is required", nameof(steps));

            return new DelegatePipeline<TInput, TOutput>(
                string.Join("→", steps.Select(s => s.Name)),
                syncWithCtx: (input, ctx) =>
                {
                    object current = input!;
                    foreach (var step in steps)
                        current = step.Execute(current, ctx);
                    return (TOutput)current!;
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    object current = input!;
                    foreach (var step in steps)
                        current = await step.ExecuteAsync(current, ctx).ConfigureAwait(false);
                    return (TOutput)current!;
                });
        }


        public static IPipeline<TInput, TOutput[]> ParallelExec<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            int degree = 4)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (degree < 1) degree = 4;

            return new DelegatePipeline<TInput, TOutput[]>(
                $"ParallelExec({degree})",
                syncWithCtx: (input, ctx) =>
                    ParallelHelper.ExecuteInParallel(degree, _ => pipeline.Execute(input, ctx), ctx.CancellationToken),
                asyncWithCtx: async (input, ctx) =>
                {
                    var tasks = Enumerable.Range(0, degree)
                        .Select(_ => (Func<Task<TOutput>>)(() => pipeline.ExecuteAsync(input, ctx)));
                    return await ParallelHelper.ExecuteInParallelAsync(tasks).ConfigureAwait(false);
                });
        }


        public static IPipeline<TInput, TOutput[]> ParallelAll<TInput, TOutput>(
            params IPipeline<TInput, TOutput>[] pipelines)
        {
            if (pipelines == null || pipelines.Length == 0)
                throw new ArgumentException("At least one pipeline is required", nameof(pipelines));

            return new DelegatePipeline<TInput, TOutput[]>(
                $"ParallelAll({pipelines.Length})",
                syncWithCtx: (input, ctx) =>
                    ParallelHelper.ExecuteInParallel(pipelines.Length, i => pipelines[i].Execute(input, ctx), ctx.CancellationToken),
                asyncWithCtx: async (input, ctx) =>
                {
                    var tasks = pipelines.Select(p => (Func<Task<TOutput>>)(() => p.ExecuteAsync(input, ctx)));
                    return await ParallelHelper.ExecuteInParallelAsync(tasks).ConfigureAwait(false);
                });
        }


        public static IPipeline<IReadOnlyList<TInput>, TOutput[]> ForEachParallel<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            int maxDegreeOfParallelism = -1)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return new DelegatePipeline<IReadOnlyList<TInput>, TOutput[]>(
                $"ForEachParallel({pipeline.Name})",
                syncWithCtx: (inputs, ctx) =>
                {
                    var options = new ParallelOptions
                    {
                        CancellationToken = ctx.CancellationToken,
                        MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : -1
                    };
                    return ParallelHelper.ExecuteInParallel(inputs.Count, i => pipeline.Execute(inputs[i], ctx), options.CancellationToken);
                },
                asyncWithCtx: async (inputs, ctx) =>
                {
                    var tasks = inputs.Select(i => (Func<Task<TOutput>>)(() => pipeline.ExecuteAsync(i, ctx)));
                    return await ParallelHelper.ExecuteInParallelAsync(tasks).ConfigureAwait(false);
                });
        }


        public static IPipeline<TInput, TOutput> Switch<TInput, TOutput, TKey>(
            Func<TInput, TKey> selector,
            Dictionary<TKey, IPipeline<TInput, TOutput>> branches,
            IPipeline<TInput, TOutput>? defaultBranch = null)
            where TKey : notnull
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (branches == null) throw new ArgumentNullException(nameof(branches));

            return new DelegatePipeline<TInput, TOutput>(
                $"Switch({branches.Count} branches)",
                syncWithCtx: (input, ctx) =>
                {
                    var key = selector(input);
                    if (branches.TryGetValue(key, out var branch))
                        return branch.Execute(input, ctx);
                    if (defaultBranch != null)
                        return defaultBranch.Execute(input, ctx);
                    throw new KeyNotFoundException($"No branch found for key '{key}'");
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    var key = selector(input);
                    if (branches.TryGetValue(key, out var branch))
                        return await branch.ExecuteAsync(input, ctx).ConfigureAwait(false);
                    if (defaultBranch != null)
                        return await defaultBranch.ExecuteAsync(input, ctx).ConfigureAwait(false);
                    throw new KeyNotFoundException($"No branch found for key '{key}'");
                });
        }


        public static IPipeline<TInput, TOutput> TryCatch<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            Func<Exception, TInput, TOutput>? catchHandler = null,
            Action<Exception>? onError = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return new DelegatePipeline<TInput, TOutput>(
                $"Try({pipeline.Name})",
                syncWithCtx: (input, ctx) =>
                {
                    try { return pipeline.Execute(input, ctx); }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                        if (catchHandler != null) return catchHandler(ex, input);
                        throw;
                    }
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    try { return await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                        if (catchHandler != null) return catchHandler(ex, input);
                        throw;
                    }
                });
        }


        public static IPipeline<TInput, TOutput> WithTimeout<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            TimeSpan timeout)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return new DelegatePipeline<TInput, TOutput>(
                $"Timeout({timeout.TotalMilliseconds}ms)",
                syncWithCtx: (input, ctx) =>
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                    timeoutCts.CancelAfter(timeout);

                    var timeoutCtx = new PipelineContext(timeoutCts.Token, ctx.ServiceProvider);
                    try
                    {
                        foreach (var kvp in ctx.Items)
                            timeoutCtx.SetItem(kvp.Key, kvp.Value!);

                        return pipeline.Execute(input, timeoutCtx);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"Pipeline '{pipeline.Name}' timed out after {timeout.TotalMilliseconds}ms");
                    }
                    finally
                    {
                        timeoutCtx.Dispose();
                    }
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                    timeoutCts.CancelAfter(timeout);

                    var timeoutCtx = new PipelineContext(timeoutCts.Token, ctx.ServiceProvider);
                    try
                    {
                        foreach (var kvp in ctx.Items)
                            timeoutCtx.SetItem(kvp.Key, kvp.Value!);

                        return await pipeline.ExecuteAsync(input, timeoutCtx).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"Pipeline '{pipeline.Name}' timed out after {timeout.TotalMilliseconds}ms");
                    }
                    finally
                    {
                        timeoutCtx.Dispose();
                    }
                });
        }


        public static IPipeline<TInput, TOutput> WithCache<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            IEqualityComparer<TInput>? keyComparer = null)
            where TInput : notnull
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            var cache = new ConcurrentDictionary<TInput, Lazy<Task<TOutput>>>(keyComparer ?? EqualityComparer<TInput>.Default);

            return new DelegatePipeline<TInput, TOutput>(
                $"Cached({pipeline.Name})",
                syncWithCtx: (input, ctx) =>
                {
                    var lazy = cache.GetOrAdd(input, _ => new Lazy<Task<TOutput>>(
                        () => Task.Run(() => pipeline.Execute(input, ctx))));
                    return lazy.Value.GetAwaiter().GetResult();
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    var lazy = cache.GetOrAdd(input, _ => new Lazy<Task<TOutput>>(
                        () => pipeline.ExecuteAsync(input, ctx)));
                    try
                    {
                        return await lazy.Value.ConfigureAwait(false);
                    }
                    catch
                    {
                        cache.TryRemove(KeyValuePair.Create(input, lazy));
                        throw;
                    }
                });
        }




        public static IPipeline<TInput, TOutput> WithRetry<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            int maxRetries = 3,
            int delayMs = 100,
            Func<Exception, int, bool>? shouldRetry = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (maxRetries < 1) maxRetries = 3;

            return new DelegatePipeline<TInput, TOutput>(
                $"Retry({maxRetries})",
                syncWithCtx: (input, ctx) =>
                {
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try { return pipeline.Execute(input, ctx); }
                        catch (Exception ex)
                        {
                            bool isLastAttempt = i == maxRetries - 1;
                            if (isLastAttempt) throw;
                            if (shouldRetry != null && !shouldRetry(ex, i)) throw;
                            if (delayMs > 0) System.Threading.Thread.Sleep(delayMs);
                        }
                    }
                    throw new InvalidOperationException($"Pipeline '{pipeline.Name}' failed after {maxRetries} retries");
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try { return await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            bool isLastAttempt = i == maxRetries - 1;
                            if (isLastAttempt) throw;
                            if (shouldRetry != null && !shouldRetry(ex, i)) throw;
                            if (delayMs > 0) await Task.Delay(delayMs, ctx.CancellationToken).ConfigureAwait(false);
                        }
                    }
                    throw new InvalidOperationException($"Pipeline '{pipeline.Name}' failed after {maxRetries} retries");
                });
        }



        public static IPipeline<TInput, TNewOutput> WithTransform<TInput, TOutput, TNewOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            ITransformer<TOutput, TNewOutput> transformer)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (transformer == null) throw new ArgumentNullException(nameof(transformer));

            var adapter = new TransformerToPipelineAdapter<TOutput, TNewOutput>(transformer);
            return pipeline.Then<TInput, TOutput, TNewOutput>(adapter);
        }

        public static IPipeline<TInput, TNewOutput> WithTransformAsync<TInput, TOutput, TNewOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            IAsyncTransformer<TOutput, TNewOutput> transformer)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (transformer == null) throw new ArgumentNullException(nameof(transformer));

            var adapter = new AsyncToPipelineAdapter<TOutput, TNewOutput>(transformer);
            return pipeline.Then<TInput, TOutput, TNewOutput>(adapter);
        }

        public static IPipeline<TInput, TOutput> WithLogging<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            Action<string>? logAction = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            logAction ??= msg => System.Diagnostics.Debug.WriteLine(msg);

            return new DelegatePipeline<TInput, TOutput>(
                $"Logged({pipeline.Name})",
                syncWithCtx: (input, ctx) =>
                {
                    logAction($"[Pipeline] {pipeline.Name} executing...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var result = pipeline.Execute(input, ctx);
                        sw.Stop();
                        logAction($"[Pipeline] {pipeline.Name} completed in {sw.ElapsedMilliseconds}ms");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        logAction($"[Pipeline] {pipeline.Name} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                        throw;
                    }
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    logAction($"[Pipeline] {pipeline.Name} executing...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var result = await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false);
                        sw.Stop();
                        logAction($"[Pipeline] {pipeline.Name} completed in {sw.ElapsedMilliseconds}ms");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        logAction($"[Pipeline] {pipeline.Name} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                        throw;
                    }
                });
        }


        public static IPipeline<TInput, TOutput> Tap<TInput, TOutput>(
            this IPipeline<TInput, TOutput> pipeline,
            Action<TInput>? before = null,
            Action<TOutput>? after = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return new DelegatePipeline<TInput, TOutput>(
                $"Tap({pipeline.Name})",
                syncWithCtx: (input, ctx) =>
                {
                    before?.Invoke(input);
                    var result = pipeline.Execute(input, ctx);
                    after?.Invoke(result);
                    return result;
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    before?.Invoke(input);
                    var result = await pipeline.ExecuteAsync(input, ctx).ConfigureAwait(false);
                    after?.Invoke(result);
                    return result;
                });
        }
    }


    [Obsolete("功能已被 StagePipeline<TContext> 覆盖，请使用 StagePipeline<TContext> 替代。")]
    public class StructureHandler

    {
        private readonly List<IPipeline<object, object>> _pipelines = new();
        private readonly Dictionary<string, object> _metadata = new();

        public int PipelineCount => _pipelines.Count;

        public IReadOnlyList<IPipeline<object, object>> Pipelines => _pipelines.AsReadOnly();

        public IReadOnlyDictionary<string, object> Metadata => _metadata;

        public StructureHandler AddPipeline(IPipeline<object, object> pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            _pipelines.Add(pipeline);
            return this;
        }

        public StructureHandler AddPipelines(IEnumerable<IPipeline<object, object>> pipelines)
        {
            if (pipelines == null) throw new ArgumentNullException(nameof(pipelines));
            foreach (var p in pipelines)
                _pipelines.Add(p);
            return this;
        }

        public StructureHandler SetMetadata(string key, object value)
        {
            _metadata[key] = value;
            return this;
        }

        public T? GetMetadata<T>(string key) where T : class
            => _metadata.TryGetValue(key, out var value) ? value as T : null;

        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();
            if (_pipelines.Count == 0)
                errors.Add("No pipelines registered.");

            for (int i = 0; i < _pipelines.Count; i++)
            {
                if (string.IsNullOrEmpty(_pipelines[i].Name))
                    errors.Add($"Pipeline at index {i} has no name.");
            }

            return errors.Count == 0;
        }

        public IPipeline<T, T> BuildSequential<T>()
        {
            if (_pipelines.Count == 0)
                throw new InvalidOperationException("No pipelines registered.");

            return new DelegatePipeline<T, T>(
                "StructureHandler",
                syncWithCtx: (input, ctx) =>
                {
                    object current = input!;
                    foreach (var p in _pipelines)
                        current = p.Execute(current, ctx);
                    return (T)current!;
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    object current = input!;
                    foreach (var p in _pipelines)
                        current = await p.ExecuteAsync(current, ctx).ConfigureAwait(false);
                    return (T)current!;
                });
        }

        public void Clear()
        {
            _pipelines.Clear();
            _metadata.Clear();
        }
    }


    public delegate Task<TResponse> RequestDelegate<TRequest, TResponse>(TRequest request);

    public delegate Task<TResponse> MiddlewareDelegate<TRequest, TResponse>(
        TRequest request, RequestDelegate<TRequest, TResponse> next);

    [Obsolete("功能已被 GenericMiddlewarePipeline<TData, TContext> 覆盖。")]
    public class MiddlewarePipeline<TRequest, TResponse>
    {
        private readonly List<MiddlewareDelegate<TRequest, TResponse>> _middlewares = new();
        private RequestDelegate<TRequest, TResponse>? _coreHandler;

        public int MiddlewareCount => _middlewares.Count;

        public MiddlewarePipeline<TRequest, TResponse> Use(
            MiddlewareDelegate<TRequest, TResponse> middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            _middlewares.Add(middleware);
            return this;
        }

        public MiddlewarePipeline<TRequest, TResponse> Use(
            Func<TRequest, Func<TRequest, Task<TResponse>>, Task<TResponse>> middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            _middlewares.Add((request, next) => middleware(request, r => next(r)));
            return this;
        }

        public MiddlewarePipeline<TRequest, TResponse> SetCoreHandler(
            RequestDelegate<TRequest, TResponse> handler)
        {
            _coreHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public RequestDelegate<TRequest, TResponse> Build()
        {
            var handler = _coreHandler ?? (request =>
                throw new InvalidOperationException("No core handler configured."));

            for (int i = _middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = _middlewares[i];
                var next = handler;
                handler = request => middleware(request, next);
            }

            return handler;
        }

        public Task<TResponse> ExecuteAsync(TRequest request)
        {
            var pipeline = Build();
            return pipeline(request);
        }

        public void Clear()
        {
            _middlewares.Clear();
            _coreHandler = null;
        }

        public IPipeline<TRequest, TResponse> ToPipeline(string? name = null)
        {
            var builtPipeline = Build();
            return new DelegatePipeline<TRequest, TResponse>(
                name ?? "MiddlewarePipeline",
                sync: request => builtPipeline(request).GetAwaiter().GetResult(),
                async: async request => await builtPipeline(request).ConfigureAwait(false));
        }

    }

    public static class MiddlewarePipelineExtensions
    {
        public static MiddlewarePipeline<TRequest, TResponse> ToMiddlewarePipeline<TRequest, TResponse>(
            this IPipeline<TRequest, TResponse> pipeline)
        {
            var middleware = new MiddlewarePipeline<TRequest, TResponse>();
            middleware.SetCoreHandler(request => pipeline.ExecuteAsync(request));
            return middleware;
        }

        public static IPipeline<TRequest, TResponse> AsPipeline<TRequest, TResponse>(
            this MiddlewarePipeline<TRequest, TResponse> middleware)
        {
            return middleware.ToPipeline();
        }
    }


    public enum OperationType
    {
        Create,
        Update,
        Delete
    }


    [Obsolete("功能已被 PipelineResult 覆盖，请使用 PipelineResult 替代。")]
    public class OperationResult
    {
        public bool Success { get; set; }

        public Exception? Error { get; set; }
    }



    [Obsolete("功能已被 GenericMiddlewarePipeline<TData, TContext> 覆盖。")]
    public class ReferenceTrackingMiddleware
    {
        public static async Task<OperationResult> ExecuteWithTrackingAsync(
            string moduleId,
            Func<Dictionary<string, object>> getStateBefore,
            Func<Task> operation,
            Func<Dictionary<string, object>>? getStateAfter = null,
            Func<string, OperationType, Dictionary<string, object>, Dictionary<string, object>, string, Task>? recordChangeAsync = null)
        {
            var before = getStateBefore();

            Exception? error = null;
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            var after = getStateAfter?.Invoke() ?? getStateBefore();

            var operationType = before.Count == 0 ? OperationType.Create :
                                after.Count == 0 ? OperationType.Delete :
                                OperationType.Update;

            if (recordChangeAsync != null)
            {
                await recordChangeAsync(moduleId, operationType, before, after, "Tracked operation");
            }

            return new OperationResult
            {
                Success = error == null,
                Error = error
            };
        }
    }


    public interface IMiddleware<TData, TContext>
    {
        string Name { get; }

        Task<TData> HandleAsync(
            TData data,
            TContext context,
            Func<TData, TContext, CancellationToken, Task<TData>> next,
            CancellationToken cancellationToken = default);
    }


    public abstract class MiddlewareBase<TData, TContext> : IMiddleware<TData, TContext>
    {
        public string Name { get; }

        protected MiddlewareBase(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public async Task<TData> HandleAsync(
            TData data,
            TContext context,
            Func<TData, TContext, CancellationToken, Task<TData>> next,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processed = await HandleCoreAsync(data, context, cancellationToken);
            return await next(processed, context, cancellationToken);
        }

        protected abstract Task<TData> HandleCoreAsync(TData data, TContext context, CancellationToken cancellationToken);
    }


    public class GenericMiddlewarePipeline<TData, TContext>
    {
        private readonly List<MiddlewareEntry<TData, TContext>> _middlewares = new();
        private Func<TData, TContext, CancellationToken, Task<TData>>? _coreHandler;

        public int MiddlewareCount => _middlewares.Count;

        public GenericMiddlewarePipeline<TData, TContext> Use(IMiddleware<TData, TContext> middleware, int order = 0)
        {
            _middlewares.Add(new MiddlewareEntry<TData, TContext>(middleware, order));
            _middlewares.Sort((a, b) => a.Order.CompareTo(b.Order));
            return this;
        }

        public GenericMiddlewarePipeline<TData, TContext> Use(
            string name,
            Func<TData, TContext, CancellationToken, Task<TData>> handler,
            int order = 0)
        {
            var middleware = new DelegateMiddleware<TData, TContext>(name, handler);
            return Use(middleware, order);
        }

        public GenericMiddlewarePipeline<TData, TContext> SetCoreHandler(
            Func<TData, TContext, CancellationToken, Task<TData>> handler)

        {
            _coreHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public async Task<TData> ExecuteAsync(TData data, TContext context, CancellationToken cancellationToken = default)
        {
            var coreHandler = _coreHandler ?? ((d, c, ct) => Task.FromResult(d));

            Func<TData, TContext, CancellationToken, Task<TData>> pipeline = coreHandler;

            for (int i = _middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = _middlewares[i].Middleware;
                var next = pipeline;
                pipeline = async (d, c, ct) =>
                    await middleware.HandleAsync(d, c, next, ct).ConfigureAwait(false);
            }

            return await pipeline(data, context, cancellationToken).ConfigureAwait(false);
        }

        public void Clear()
        {
            _middlewares.Clear();
            _coreHandler = null;
        }
    }

    internal class MiddlewareEntry<TData, TContext>
    {
        public IMiddleware<TData, TContext> Middleware { get; }
        public int Order { get; }

        public MiddlewareEntry(IMiddleware<TData, TContext> middleware, int order)
        {
            Middleware = middleware;
            Order = order;
        }
    }

    internal class DelegateMiddleware<TData, TContext> : IMiddleware<TData, TContext>
    {
        public string Name { get; }
        private readonly Func<TData, TContext, CancellationToken, Task<TData>> _handler;

        public DelegateMiddleware(string name, Func<TData, TContext, CancellationToken, Task<TData>> handler)
        {
            Name = name;
            _handler = handler;
        }

        public async Task<TData> HandleAsync(
            TData data,
            TContext context,
            Func<TData, TContext, CancellationToken, Task<TData>> next,
            CancellationToken cancellationToken = default)
        {
            var processed = await _handler(data, context, cancellationToken).ConfigureAwait(false);
            return await next(processed, context, cancellationToken).ConfigureAwait(false);
        }
    }
}




