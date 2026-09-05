
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TConfig;

namespace Modules.Pipeline
{

    public static class PipelineUsage
    {

        public static IPipeline<TIn, TOut> Create<TIn, TOut>(
            string name,
            Func<TIn, TOut> sync,
            Func<TIn, Task<TOut>>? async = null,
            string? description = null)
        {
            return new DelegatePipeline<TIn, TOut>(name, sync, async, description);
        }

        public static IPipeline<TIn, TOut> CreateWithContext<TIn, TOut>(
            string name,
            Func<TIn, PipelineContext, TOut> syncWithCtx,
            Func<TIn, PipelineContext, Task<TOut>>? asyncWithCtx = null,
            string? description = null)
        {
            return new DelegatePipeline<TIn, TOut>(name, syncWithCtx, asyncWithCtx, description);
        }

        public static IPipeline<T, T> CreateStage<T>(
            string name,
            Action<T, PipelineContext> action)
        {
            return new DelegatePipeline<T, T>(
                name,
                syncWithCtx: (input, ctx) =>
                {
                    action(input, ctx);
                    return input;
                },
                asyncWithCtx: async (input, ctx) =>
                {
                    await Task.Run(() => action(input, ctx)).ConfigureAwait(false);
                    return input;
                });
        }

        public static IPipeline<T, T> CreateIdentity<T>(string? name = null)
        {
            return new DelegatePipeline<T, T>(
                name ?? "Identity",
                sync: input => input,
                async: input => Task.FromResult(input));
        }


        public static IPipeline<TIn, TOut> Then<TIn, TMid, TOut>(
            IPipeline<TIn, TMid> first,
            IPipeline<TMid, TOut> next)
        {
            return first.Then(next);
        }

        public static IPipeline<T, T> Branch<T>(
            Func<T, bool> predicate,
            IPipeline<T, T> trueBranch,
            IPipeline<T, T> falseBranch)
        {
            return predicate.Branch(trueBranch, falseBranch);
        }

        public static IPipeline<T, T> Loop<T>(
            Func<T, bool> predicate,
            IPipeline<T, T> body)
        {
            return predicate.Loop(body);
        }

        public static IPipeline<TIn, TOut> Compose<TIn, TOut>(
            params IPipeline<object, object>[] steps)
        {
            return PipelineExtensions.Compose<TIn, TOut>(steps);
        }

        public static IPipeline<TIn, TOut> Switch<TIn, TOut, TKey>(
            Func<TIn, TKey> selector,
            Dictionary<TKey, IPipeline<TIn, TOut>> branches,
            IPipeline<TIn, TOut>? defaultBranch = null)
            where TKey : notnull
        {
            return PipelineExtensions.Switch(selector, branches, defaultBranch);
        }


        public static TOut Run<TIn, TOut>(
            IPipeline<TIn, TOut> pipeline,
            TIn input,
            PipelineContext? context = null)
        {
            return pipeline.Execute(input, context);
        }

        public static Task<TOut> RunAsync<TIn, TOut>(
            IPipeline<TIn, TOut> pipeline,
            TIn input,
            PipelineContext? context = null)
        {
            return pipeline.ExecuteAsync(input, context);
        }

        public static PipelineResult RunAndGetResult<TIn, TOut>(
            IPipeline<TIn, TOut> pipeline,
            TIn input,
            PipelineContext? context = null)
        {
            var ctx = context ?? CreateContext();
            var errors = new ErrorCollector();
            try
            {
                var result = pipeline.Execute(input, ctx);
                return PipelineResult.Successful(result, ctx);
            }
            catch (Exception ex)
            {
                errors.Add("EXECUTION_ERROR", ex.Message, ErrorLevel.Fatal, ex);
                return PipelineResult.Failed(null, errors, ctx);
            }
        }


        public static ErrorCollector CreateErrorCollector()
        {
            return new ErrorCollector();
        }

        public static Guard CreateGuard()
        {
            return new Guard();
        }

        public static IPipeline<TIn, TOut> WithErrorHandling<TIn, TOut>(
            IPipeline<TIn, TOut> pipeline,
            ErrorCollector errorCollector)
        {
            return pipeline.WithErrorHandling(errorCollector);
        }

        public static IPipeline<T, T> WithLifecycle<T>(
            IPipeline<T, T> pipeline,
            Action<T>? onBefore = null,
            Action<T>? onAfter = null,
            Action<Exception>? onError = null)
        {
            return pipeline.WithLifecycle(onBefore, onAfter, onError);
        }

        public static bool CaptureException(
            Exception ex,
            ErrorCollector errors,
            string stageName,
            bool isFatal = false)
        {
            return PipelineErrorHandler.CaptureException(ex, errors, stageName, isFatal);
        }

        public static bool RecordStageFailure(
            ErrorCollector errors,
            string stageName,
            bool isFatal = false)
        {
            return PipelineErrorHandler.RecordStageFailure(errors, stageName, isFatal);
        }


        public static IPipeline<TInput, TOutput> WithTimeout<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            TimeSpan timeout)
        {
            return pipeline.WithTimeout(timeout);
        }

        public static IPipeline<TInput, TOutput> WithCache<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            IEqualityComparer<TInput>? keyComparer = null)
            where TInput : notnull
        {
            return pipeline.WithCache(keyComparer);
        }

        public static IPipeline<TInput, TOutput> WithLogging<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            Action<string>? logAction = null)
        {
            return pipeline.WithLogging(logAction);
        }

        public static IPipeline<TInput, TOutput> Tap<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            Action<TInput>? before = null,
            Action<TOutput>? after = null)
        {
            return pipeline.Tap(before, after);
        }

        public static IPipeline<TInput, TOutput[]> ParallelExec<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            int degree = 4)
        {
            return pipeline.ParallelExec(degree);
        }

        public static IPipeline<TInput, TOutput[]> ParallelAll<TInput, TOutput>(
            params IPipeline<TInput, TOutput>[] pipelines)
        {
            return PipelineExtensions.ParallelAll(pipelines);
        }

        public static IPipeline<IReadOnlyList<TInput>, TOutput[]> ForEachParallel<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline,
            int maxDegreeOfParallelism = -1)
        {
            return pipeline.ForEachParallel(maxDegreeOfParallelism);
        }

        public static GenericMiddlewarePipeline<TData, TContext> CreateMiddlewarePipeline<TData, TContext>()
        {
            return new GenericMiddlewarePipeline<TData, TContext>();
        }

        [Obsolete("功能已被 GenericMiddlewarePipeline<TData, TContext> 覆盖，请使用 CreateMiddlewarePipeline<TData, TContext>() 替代。")]
        public static MiddlewarePipeline<TRequest, TResponse> CreateLegacyMiddlewarePipeline<TRequest, TResponse>()
        {
            return new MiddlewarePipeline<TRequest, TResponse>();
        }

        public static ForEachPipeline<TContext, TItem> ForEach<TContext, TItem>(
            Func<TContext, IReadOnlyList<TItem>> itemsSelector,
            IPipeline<TContext, TContext> itemPipeline,
            Action<TContext, TItem, int>? onItemBefore = null,
            Action<TContext, TItem, int, bool>? onItemAfter = null,
            int? maxCount = null,
            bool breakOnError = false,
            string? name = null)
        {
            return new ForEachPipeline<TContext, TItem>(
                itemsSelector, itemPipeline,
                onItemBefore, onItemAfter,
                maxCount, breakOnError, name);
        }

        public static ParallelPipeline<TContext> Parallel<TContext>(
            IReadOnlyList<IPipeline<TContext, TContext>> pipelines,
            int maxDegreeOfParallelism = 0,
            bool continueOnError = true,
            string? name = null)
        {
            return new ParallelPipeline<TContext>(
                pipelines, maxDegreeOfParallelism, continueOnError, name);
        }

        public static PipelineTryCatch<TContext> TryCatch<TContext>(
            IPipeline<TContext, TContext> mainPipeline,
            IPipeline<TContext, TContext>? fallbackPipeline = null,
            Func<Exception, bool>? exceptionFilter = null,
            Action<Exception>? onError = null,
            string? name = null)
        {
            return new PipelineTryCatch<TContext>(
                mainPipeline, fallbackPipeline, exceptionFilter, onError, name);
        }

        public static PipelineRetry<TContext> Retry<TContext>(
            IPipeline<TContext, TContext> innerPipeline,
            int maxRetries = 3,
            int delayMs = 100,
            Func<Exception, int, bool>? shouldRetry = null,
            Action<Exception, int>? onRetry = null,
            string? name = null)
        {
            return new PipelineRetry<TContext>(
                innerPipeline, maxRetries, delayMs, shouldRetry, onRetry, name);
        }

        public static ConditionalPipeline<TContext> Conditional<TContext>(
            IPipeline<TContext, TContext> innerPipeline,
            Func<TContext, bool> condition,
            string? name = null)
        {
            return new ConditionalPipeline<TContext>(innerPipeline, condition, name);
        }

        public static PipelineBranch<TContext> Branch<TContext>(
            Func<TContext, bool> condition,
            IPipeline<TContext, TContext> trueBranch,
            IPipeline<TContext, TContext> falseBranch,
            string? name = null)
        {
            return new PipelineBranch<TContext>(condition, trueBranch, falseBranch, name);
        }

        public static PipelineSwitch<TContext, TKey> Switch<TContext, TKey>(
            Func<TContext, TKey> selector,
            Dictionary<TKey, IPipeline<TContext, TContext>> branches,
            IPipeline<TContext, TContext>? defaultBranch = null,
            string? name = null)
            where TKey : notnull
        {
            return new PipelineSwitch<TContext, TKey>(selector, branches, defaultBranch, name);
        }

        public static PipelineAdapter<TContext, TIn, TOut> Adapt<TContext, TIn, TOut>(
            IPipeline<TIn, TOut> innerPipeline,
            Func<TContext, TIn> inputSelector,
            Action<TContext, TOut> outputWriter,
            string? name = null)
        {
            return new PipelineAdapter<TContext, TIn, TOut>(
                innerPipeline, inputSelector, outputWriter, name);
        }


        public static Pipeline CreatePipeline()
        {
            return new Pipeline();
        }

        public static Pipeline<TIn, TOut> CreatePipeline<TIn, TOut>()
            where TOut : class
        {
            return new Pipeline<TIn, TOut>();
        }

        public static IStagePipeline<TContext> CreateStagePipeline<TContext>(
            string name,
            string? description = null)
            where TContext : class, IPipelineContext
        {
            return new StagePipeline<TContext>(name, description);
        }


        public static PipelineContext CreateContext(
            CancellationToken cancellationToken = default,
            IServiceProvider? services = null)
        {
            return new PipelineContext(cancellationToken, services);
        }

        public static Session<TTarget, TConfig, TContext> CreateSession<TTarget, TConfig, TContext>(
            TConfig config,
            TContext context,
            IPipeline<TTarget, TTarget> pipeline)
            where TTarget : class
            where TContext : PipelineContext
        {
            return new Session<TTarget, TConfig, TContext>(config, context, pipeline);
        }

        public static SessionManager<TTarget, TConfig, TContext> CreateSessionManager<TTarget, TConfig, TContext>()
            where TTarget : class
            where TContext : PipelineContext
        {
            return new SessionManager<TTarget, TConfig, TContext>();
        }

        public static EventSubscriptionManager<TTarget> CreateEventSubscriptionManager<TTarget>()
            where TTarget : class
        {
            return new EventSubscriptionManager<TTarget>();
        }


        public static StrategyRegistry<TKey, TInput, TOutput> CreateStrategyRegistry<TKey, TInput, TOutput>()
            where TKey : notnull
        {
            return new StrategyRegistry<TKey, TInput, TOutput>();
        }

        public static PipelineStageRegistry CreateStageRegistry()
        {
            return new PipelineStageRegistry();
        }


        public static PipelineResult SuccessResult(object? data = null)
        {
            return PipelineResult.Successful(data);
        }

        public static PipelineResult FailureResult(ErrorCollector errors)
        {
            return PipelineResult.Failed(errors);
        }

        public static PipelineResult AbortedResult(ErrorCollector errors)
        {
            return PipelineResult.Aborted(errors);
        }

        public static PipelineResult WarningResult(object? data, ErrorCollector errors, PipelineContext context)
        {
            return PipelineResult.CompletedWithWarnings(data, errors, context);
        }

        public static PipelineResult<T> SuccessResult<T>(T data)
            where T : class
        {
            return PipelineResult<T>.Successful(data);
        }

        public static PipelineResult<T> FailureResult<T>(ErrorCollector errors)
            where T : class
        {
            return PipelineResult<T>.Failed(errors);
        }
    }
}
