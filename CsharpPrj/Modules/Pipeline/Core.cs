
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;
using TConfig;

namespace Modules.Pipeline
{

    public interface IPipeline<in TInput, TOutput> : ISkeleton<TInput, TOutput>
    {
        new string Name { get; }

        TOutput Execute(TInput input, PipelineContext? context = null);

        Task<TOutput> ExecuteAsync(TInput input, PipelineContext? context = null);
    }


    public interface IStage<TTarget, TConfig, TContext>
        where TContext : PipelineContext
    {
        string Name { get; }

        void Execute(TTarget target, TConfig config, TContext context);
    }


    public class PipelineContext : SkeletonContext, IPipelineContext
    {
        private readonly Dictionary<string, bool> _stageSuccess = new();
        private readonly Dictionary<string, string?> _stageErrors = new();
        private CancellationTokenSource? _cancelCts;


        public string Id { get; }

        public DateTime CreatedAt { get; }

        public bool IsCancelled => CancellationToken.IsCancellationRequested;

        public string? CancelReason { get; set; }

        public string? SourceFile { get; set; }


        public string Mode { get; set; } = "Default";

        public IReadOnlyDictionary<string, bool> StageSuccess =>
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool>(_stageSuccess);

        public IReadOnlyDictionary<string, string?> StageErrors =>
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(_stageErrors);

        public IDictionary<string, object?> SharedData => (IDictionary<string, object?>)Items;

        public virtual bool HasError => _stageErrors.Values.Any(e => e != null);

        public string? FirstError => _stageErrors.Values.FirstOrDefault(e => e != null);

        public void SetStageResult(string stageName, bool success, string? error = null)
        {
            _stageSuccess[stageName] = success;
            _stageErrors[stageName] = error;
        }

        public bool GetStageSuccess(string stageName)
            => _stageSuccess.TryGetValue(stageName, out var success) && success;

        public string? GetStageError(string stageName)
            => _stageErrors.TryGetValue(stageName, out var error) ? error : null;

        public bool IsStageExplicitlySet(string stageName)
            => _stageSuccess.ContainsKey(stageName);

        public virtual void Reset()
        {
            _stageSuccess.Clear();
            _stageErrors.Clear();
            foreach (var key in Items.Keys.ToList())
                RemoveItem(key);
            Mode = "Default";
            SourceFile = null;
            CancelReason = null;
        }


        public PipelineContext(CancellationToken externalToken = default, IServiceProvider? services = null)
            : base(null)
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.UtcNow;
            if (externalToken != default)
                CancellationToken = externalToken;
            if (services != null)
                ServiceProvider = services;
        }

        public void Cancel(string? reason = null)
        {
            CancelReason = reason;
            if (_cancelCts != null && !_cancelCts.IsCancellationRequested)
            {
                _cancelCts.Cancel();
                CancellationToken = _cancelCts.Token;
                return;
            }
            _cancelCts?.Dispose();
            _cancelCts = new CancellationTokenSource();
            _cancelCts.Cancel();
            CancellationToken = _cancelCts.Token;
        }


        public T? GetItem<T>(string key) where T : class
            => Items.TryGetValue(key, out var value) ? value as T : null;

        public void SetItem<T>(string key, T value) where T : class
            => base.SetItem(key, value);

        public void SetRange(IEnumerable<KeyValuePair<string, object?>> items)
        {
            foreach (var kv in items)
                if (kv.Value != null)
                    SetItem(kv.Key, kv.Value);
        }
    }




    public abstract class PipelineBase<TInput, TOutput> : SkeletonBase<TInput, TOutput>, IPipeline<TInput, TOutput>
    {
        protected PipelineBase(string? name = null, string? description = null)
            : base(name, description) { }

        public TOutput Execute(TInput input, PipelineContext? context = null)
        {
            var skelCtx = BridgeToSkeletonContext(context);
            return base.Execute(input, skelCtx);
        }

        public async Task<TOutput> ExecuteAsync(TInput input, PipelineContext? context = null)
        {
            var skelCtx = BridgeToSkeletonContext(context);
            return await base.ExecuteAsync(input, skelCtx).ConfigureAwait(false);
        }

        private static SkeletonContext BridgeToSkeletonContext(PipelineContext? pipelineCtx)
        {
            if (pipelineCtx == null) return new SkeletonContext();
            return pipelineCtx;
        }

        protected abstract TOutput ExecuteCore(TInput input, PipelineContext context);

        protected abstract Task<TOutput> ExecuteCoreAsync(TInput input, PipelineContext context);

        protected sealed override TOutput ExecuteCore(TInput input, SkeletonContext context)
        {
            var pipelineCtx = context as PipelineContext ?? new PipelineContext(context.CancellationToken, context.ServiceProvider);
            return ExecuteCore(input, pipelineCtx);
        }

        protected sealed override async Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context)
        {
            var pipelineCtx = context as PipelineContext ?? new PipelineContext(context.CancellationToken, context.ServiceProvider);
            return await ExecuteCoreAsync(input, pipelineCtx).ConfigureAwait(false);
        }
    }


    public class DelegatePipeline<TInput, TOutput> : PipelineBase<TInput, TOutput>
    {
        private readonly DelegateSkeleton<TInput, TOutput> _inner;
        private readonly Func<TInput, PipelineContext, TOutput>? _syncWithCtx;
        private readonly Func<TInput, PipelineContext, Task<TOutput>>? _asyncWithCtx;

        public DelegatePipeline(
            string? name = null,
            Func<TInput, TOutput>? sync = null,
            Func<TInput, Task<TOutput>>? async = null,
            string? description = null)
            : base(name, description)
        {
            _inner = new DelegateSkeleton<TInput, TOutput>(name, sync, async, description);
        }

        public DelegatePipeline(
            string? name,
            Func<TInput, PipelineContext, TOutput>? syncWithCtx = null,
            Func<TInput, PipelineContext, Task<TOutput>>? asyncWithCtx = null,
            string? description = null)
            : base(name, description)
        {
            _syncWithCtx = syncWithCtx;
            _asyncWithCtx = asyncWithCtx;
            _inner = new DelegateSkeleton<TInput, TOutput>(name,
                syncWithCtx != null
                    ? (Func<TInput, TOutput>?)(i => {
                        throw new InvalidOperationException(
                            "DelegatePipeline with context should be executed via Execute(input, context) method.");
                      })
                    : null,
                asyncWithCtx != null
                    ? (Func<TInput, Task<TOutput>>?)(async i => {
                        throw new InvalidOperationException(
                            "DelegatePipeline with context should be executed via ExecuteAsync(input, context) method.");
                      })
                    : null,
                description);
        }


        protected override TOutput ExecuteCore(TInput input, PipelineContext ctx)
        {
            if (_syncWithCtx != null)
                return _syncWithCtx(input, ctx);
            return _inner.Execute(input);
        }

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, PipelineContext ctx)
        {
            if (_asyncWithCtx != null)
                return _asyncWithCtx(input, ctx);
            return _inner.ExecuteAsync(input);
        }
    }



    public enum PipelineResultStatus
    {
        Success,
        Failed,
        Aborted,
        PartialSuccess
    }


    public class PipelineResult
    {
        public PipelineResultStatus Status { get; }

        public object? Data { get; }

        public ErrorCollector Errors { get; }

        public PipelineContext Context { get; }

        public bool IsSuccess => Status == PipelineResultStatus.Success
                              || Status == PipelineResultStatus.PartialSuccess;

        public bool IsAborted => Status == PipelineResultStatus.Aborted;

        public bool HasErrors => Errors.HasErrors;

        public bool HasFatalErrors => Errors.HasFatalErrors;

        public PipelineResult(
            object? data,
            ErrorCollector errors,
            PipelineContext context,
            PipelineResultStatus status = PipelineResultStatus.Success)
        {
            Data = data;
            Errors = errors ?? new ErrorCollector();
            Context = context ?? new PipelineContext();
            Status = status;
        }

        public static PipelineResult Successful(object? data = null)
            => new(data, new ErrorCollector(), new PipelineContext(), PipelineResultStatus.Success);

        public static PipelineResult Successful(object? data, PipelineContext context)
            => new(data, new ErrorCollector(), context, PipelineResultStatus.Success);

        public static PipelineResult Failed(ErrorCollector errors)
            => new(null, errors, new PipelineContext(), PipelineResultStatus.Failed);

        public static PipelineResult Failed(object? data, ErrorCollector errors, PipelineContext context)
            => new(data, errors, context, PipelineResultStatus.Failed);

        public static PipelineResult Aborted(ErrorCollector errors)
            => new(null, errors, new PipelineContext(), PipelineResultStatus.Aborted);

        public static PipelineResult Aborted(ErrorCollector errors, PipelineContext context)
            => new(null, errors, context, PipelineResultStatus.Aborted);

        public static PipelineResult CompletedWithWarnings(object? data, ErrorCollector errors, PipelineContext context)
            => new(data, errors, context, PipelineResultStatus.PartialSuccess);

        public PipelineResult<T> ToGeneric<T>() where T : class
            => new(IsSuccess, IsAborted, Data as T, Data as T, Errors)
            {
                Status = Status
            };

    }

    public class PipelineResult<T> where T : class
    {
        public PipelineResultStatus Status { get; set; } = PipelineResultStatus.Success;

        public bool IsSuccess { get; }

        public bool IsAborted { get; }

        public T? Data { get; }

        public T? RawData { get; }

        public ErrorCollector Errors { get; }

        public bool HasErrors => Errors.HasErrors;

        public bool HasFatalErrors => Errors.HasFatalErrors;

        internal PipelineResult(bool isSuccess, bool isAborted, T? data, T? rawData, ErrorCollector errors)
        {
            IsSuccess = isSuccess;
            IsAborted = isAborted;
            Data = data;
            RawData = rawData;
            Errors = errors ?? new ErrorCollector();
        }

        public static PipelineResult<T> Successful(T data)
            => new(true, false, data, data, new ErrorCollector());

        public static PipelineResult<T> Successful(T data, T rawData)
            => new(true, false, data, rawData, new ErrorCollector());

        public static PipelineResult<T> Failed(ErrorCollector errors)
            => new(false, false, default, default, errors);

        public static PipelineResult<T> Aborted(ErrorCollector errors)
            => new(false, true, default, default, errors);

        public static PipelineResult<T> CompletedWithWarnings(T data, T rawData, ErrorCollector errors)
            => new(true, false, data, rawData, errors);

        public PipelineResult ToNonGeneric(PipelineContext context)
            => new(Data, Errors, context, Status);

    }



    public static class PipelineErrorHandler
    {
        public const string DefaultCodePrefix = "PIPELINE";

        public static bool CaptureException(
            Exception ex,
            ErrorCollector errors,
            string stageName,
            bool isFatal,
            string? codePrefix = null)
        {
            var prefix = codePrefix ?? DefaultCodePrefix;
            var code = $"{prefix}_{stageName.ToUpperInvariant()}_EXCEPTION";
            errors.Add(code, $"{stageName} 抛出异常：{ex.Message}",
                isFatal ? ErrorLevel.Fatal : ErrorLevel.Error, ex);
            return isFatal;
        }

        public static bool RecordStageFailure(
            ErrorCollector errors,
            string stageName,
            bool isFatal,
            string? codePrefix = null)
        {
            var prefix = codePrefix ?? DefaultCodePrefix;
            var code = $"{prefix}_{stageName.ToUpperInvariant()}_FAILED";
            errors.Add(code, $"{stageName} 处理失败，返回 null",
                isFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
            return isFatal;
        }

        public static PipelineResult CreateAbortedResult(ErrorCollector errors, PipelineContext context)
            => PipelineResult.Aborted(errors, context);

        public static PipelineResult CreateFailedResult(object? data, ErrorCollector errors, PipelineContext context)
            => PipelineResult.Failed(data, errors, context);

        public static PipelineResult CreateSuccessResult(object? data, PipelineContext context)
            => PipelineResult.Successful(data, context);

        public static PipelineResult CreateWarningResult(object? data, ErrorCollector errors, PipelineContext context)
            => PipelineResult.CompletedWithWarnings(data, errors, context);

        public static PipelineResult DetermineResult(
            object? data,
            ErrorCollector errors,
            PipelineContext context,
            bool hasNonFatalErrors)
        {
            if (errors.HasFatalErrors)
                return CreateAbortedResult(errors, context);
            if (errors.HasRealErrors || hasNonFatalErrors)
                return CreateFailedResult(data, errors, context);
            if (errors.HasWarnings || errors.HasInfos)
                return CreateWarningResult(data, errors, context);
            return CreateSuccessResult(data, context);
        }
    }



    public interface IPipelineStage<TInput, TOutput>
    {
        string Name { get; }

        int Order { get; }

        bool IsFatal { get; }

        TOutput? Process(TInput input, ErrorCollector errors, PipelineContext context);
    }

    public interface IPipelineStage
    {
        string Name { get; }

        int Order { get; }

        bool IsFatal { get; }

        object? Process(object input, ErrorCollector errors, PipelineContext context);
    }


    public abstract class StageCollection<TStage>
    {
        protected readonly List<TStage> _stages = new();

        public IReadOnlyList<TStage> Stages => _stages.AsReadOnly();

        public int Count => _stages.Count;

        protected void AddAndSort(TStage stage, Func<TStage, int> orderSelector)
        {
            _stages.Add(stage);
            _stages.Sort((a, b) => orderSelector(a).CompareTo(orderSelector(b)));
        }

        protected void AddRangeAndSort(IEnumerable<TStage> stages, Func<TStage, int> orderSelector)
        {
            _stages.AddRange(stages);
            _stages.Sort((a, b) => orderSelector(a).CompareTo(orderSelector(b)));
        }

        protected bool InsertBefore(string stageName, TStage stage, Func<TStage, string> nameSelector)
        {
            var index = _stages.FindIndex(s =>
                string.Equals(nameSelector(s), stageName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _stages.Insert(index, stage);
                return true;
            }
            return false;
        }

        protected bool InsertAfter(string stageName, TStage stage, Func<TStage, string> nameSelector)
        {
            var index = _stages.FindIndex(s =>
                string.Equals(nameSelector(s), stageName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _stages.Insert(index + 1, stage);
                return true;
            }
            return false;
        }

        protected int Remove(string stageName, Func<TStage, string> nameSelector)
            => _stages.RemoveAll(s =>
                string.Equals(nameSelector(s), stageName, StringComparison.OrdinalIgnoreCase));

        public void Clear() { _stages.Clear(); }
    }



    public class Pipeline : StageCollection<IPipelineStage>
    {
        protected readonly ErrorCollector _globalErrors = new();

        public Pipeline RegisterStage(IPipelineStage stage)
        {
            AddAndSort(stage, s => s.Order);
            return this;
        }

        public Pipeline RegisterStages(IEnumerable<IPipelineStage> stages)
        {
            AddRangeAndSort(stages, s => s.Order);
            return this;
        }

        public PipelineResult Execute(object input, string? sourceFile = null)
            => ExecuteCore(input, sourceFile);

        public async Task<PipelineResult> ExecuteAsync(
            object input, string? sourceFile = null, CancellationToken cancellationToken = default)
        {
            foreach (var stage in _stages)
                cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();

            return ExecuteCore(input, sourceFile, cancellationToken);
        }

        private PipelineResult ExecuteCore(
            object input, string? sourceFile, CancellationToken cancellationToken = default)
        {
            var context = new PipelineContext(cancellationToken) { SourceFile = sourceFile };
            var errors = new ErrorCollector();
            object? document = input;
            bool hasNonFatalErrors = false;

            ExecuteStages(_stages, ref document, errors, context, ref hasNonFatalErrors);

            errors.Merge(_globalErrors);

            if (errors.HasFatalErrors)
                return PipelineResult.Aborted(errors, context);
            if (errors.HasRealErrors || hasNonFatalErrors)
                return PipelineResult.Failed(document, errors, context);
            if (errors.HasWarnings || errors.HasInfos)
                return PipelineResult.CompletedWithWarnings(document!, errors, context);

            return PipelineResult.Successful(document!, context);
        }

        protected static void ExecuteStages(
            IReadOnlyList<IPipelineStage> stages,
            ref object? document,
            ErrorCollector errors,
            PipelineContext context,
            ref bool hasNonFatalErrors)
        {
            Type? expectedType = null;

            foreach (var stage in stages)
            {
                try
                {
                    if (document != null && expectedType != null && document.GetType() != expectedType)
                    {
                        var actualType = document.GetType().Name;
                        errors.Add($"STAGE_{stage.Name.ToUpper()}_TYPE_CHAIN_BREAK",
                            $"链式类型断裂: Stage '{stage.Name}' 输入类型 {actualType} " +
                            $"与期望类型 {expectedType.Name} 不匹配。",
                            ErrorLevel.Warning);
                    }

                    document = stage.Process(document, errors, context);

                    if (document == null)
                    {
                        PipelineStageDiagnostics.ReportNullResult(errors, stage.Name, stage.IsFatal);

                        if (stage.IsFatal)
                            return;

                        hasNonFatalErrors = true;
                    }
                    else
                    {
                        expectedType = document.GetType();
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"STAGE_{stage.Name.ToUpper()}_EXCEPTION",
                        $"Stage '{stage.Name}' 抛出异常：{ex.Message}",
                        stage.IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error,
                        ex);

                    if (stage.IsFatal)
                        return;

                    hasNonFatalErrors = true;
                }
            }
        }
    }


    public class Pipeline<TInput, TOutput> : StageCollection<IPipelineStage<TInput, TOutput>>
        where TOutput : class
    {
        protected readonly ErrorCollector _globalErrors = new();

        public Pipeline<TInput, TOutput> RegisterStage(IPipelineStage<TInput, TOutput> stage)
        {
            AddAndSort(stage, s => s.Order);
            return this;
        }

        public Pipeline<TInput, TOutput> RegisterStages(IEnumerable<IPipelineStage<TInput, TOutput>> stages)
        {
            AddRangeAndSort(stages, s => s.Order);
            return this;
        }

        public PipelineResult<TOutput> Execute(TInput input, string? sourceFile = null)
        {
            var context = new PipelineContext { SourceFile = sourceFile };
            var errors = new ErrorCollector();
            TOutput? result = default;
            bool hasNonFatalErrors = false;

            ExecuteStages(_stages, input, ref result, errors, context, ref hasNonFatalErrors);

            errors.Merge(_globalErrors);

            if (errors.HasFatalErrors)
                return PipelineResult<TOutput>.Aborted(errors);
            if (errors.HasRealErrors || hasNonFatalErrors)
                return PipelineResult<TOutput>.Failed(errors);

            if (result == null)
            {
                errors.Add("ALL_STAGES_FAILED", "所有 Stage 均返回 null，无法生成有效结果", ErrorLevel.Error);
                return PipelineResult<TOutput>.Failed(errors);
            }

            if (errors.HasWarnings || errors.HasInfos)
                return PipelineResult<TOutput>.CompletedWithWarnings(result, result, errors);

            return PipelineResult<TOutput>.Successful(result, result);
        }

        protected static void ExecuteStages(
            IReadOnlyList<IPipelineStage<TInput, TOutput>> stages,
            TInput input,
            ref TOutput? result,
            ErrorCollector errors,
            PipelineContext context,
            ref bool hasNonFatalErrors)
        {
            foreach (var stage in stages)
            {
                try
                {
                    result = stage.Process(input, errors, context);

                    if (result == null)
                    {
                        PipelineStageDiagnostics.ReportNullResult(errors, stage.Name, stage.IsFatal);

                        if (stage.IsFatal)
                            return;

                        hasNonFatalErrors = true;
                        result = default;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"STAGE_{stage.Name.ToUpper()}_EXCEPTION",
                        $"Stage '{stage.Name}' 抛出异常：{ex.Message}",
                        stage.IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error,
                        ex);

                    if (stage.IsFatal)
                        return;

                    hasNonFatalErrors = true;
                    result = default;
                }
            }
        }
    }



    /// <summary>阶段诊断构造复用（D5）：两 <c>ExecuteStages</c> 的「返回 null」失败诊断单一来源。</summary>
    internal static class PipelineStageDiagnostics
    {
        public static void ReportNullResult(ErrorCollector errors, string stageName, bool isFatal)
            => errors.Add($"STAGE_{stageName.ToUpper()}_FAILED",
                $"Stage '{stageName}' 处理失败，返回 null",
                isFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
    }

    public abstract class PipelineStageBase<TContext> : IPipelineStage<TContext>
        where TContext : IPipelineContext
    {
        public string Name { get; }

        public string Description { get; }

        protected PipelineStageBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? $"{name} stage";
        }

        public virtual bool CanExecute(TContext context)
        {
            return true;
        }

        public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteCoreAsync(context, cancellationToken);
                context.SetStageResult(Name, true);
            }
            catch (OperationCanceledException)
            {
                context.SetStageResult(Name, false, $"{Name} was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                context.SetStageResult(Name, false, $"{Name} error: {ex.Message}");
                throw;
            }
        }

        protected abstract Task ExecuteCoreAsync(TContext context, CancellationToken cancellationToken);
    }


    public class StagePipeline<TContext> : StageCollection<IPipelineStage<TContext>>, IStagePipeline<TContext>
        where TContext : IPipelineContext
    {
        private readonly string _name;
        private readonly string _description;

        public string Name => _name;

        public string Description => _description;

        public StagePipeline(string name, string? description = null)
        {
            _name = name ?? "UnnamedStagePipeline";
            _description = description ?? $"{_name} pipeline";
        }

        public IStagePipeline<TContext> Register(IPipelineStage<TContext> stage)
        {
            ArgumentNullException.ThrowIfNull(stage);
            _stages.Add(stage);
            return this;
        }

        public IStagePipeline<TContext> RegisterRange(IEnumerable<IPipelineStage<TContext>> stages)
        {
            ArgumentNullException.ThrowIfNull(stages);
            _stages.AddRange(stages);
            return this;
        }

        public IStagePipeline<TContext> InsertBefore(string stageName, IPipelineStage<TContext> stage)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if (!InsertBefore(stageName, stage, s => s.Name))
                throw new KeyNotFoundException($"Cannot find stage '{stageName}' to insert before.");
            return this;
        }

        public IStagePipeline<TContext> InsertAfter(string stageName, IPipelineStage<TContext> stage)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if (!InsertAfter(stageName, stage, s => s.Name))
                throw new KeyNotFoundException($"Cannot find stage '{stageName}' to insert after.");
            return this;
        }

        public IStagePipeline<TContext> Remove(string stageName)
        {
            Remove(stageName, s => s.Name);
            return this;
        }


        public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            foreach (var stage in _stages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!stage.CanExecute(context))
                {
                    context.SetStageResult(stage.Name, true);
                    continue;
                }

                await stage.ExecuteAsync(context, cancellationToken);

                if (!context.GetStageSuccess(stage.Name))
                {
                    break;
                }
            }
        }


        public async Task ExecuteForEachAsync(
            TContext context,
            IEnumerable<string> items,
            Action<TContext, string, int>? onItemBefore = null,
            Action<TContext, string, int, bool>? onItemAfter = null,
            CancellationToken cancellationToken = default)
        {
            var itemList = items?.ToList() ?? new List<string>();
            var index = 0;

            foreach (var item in itemList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Reset();

                onItemBefore?.Invoke(context, item, index);

                await ExecuteAsync(context, cancellationToken);

                var success = !context.HasError;
                onItemAfter?.Invoke(context, item, index, success);

                index++;
            }
        }
    }

}
