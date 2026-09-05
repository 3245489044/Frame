
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Pipeline
{

    public interface IAsyncLifecycle
    {
        bool IsInitialized { get; }

        Task? InitializationTask { get; }

        Task OnInitializeAsync();

        void OnInitialized();

        void OnInitializeFailed(Exception ex);
    }

    public interface ICancellableAsyncLifecycle : IAsyncLifecycle
    {
        CancellationToken CancellationToken { get; }
    }


    public enum BindingMode
    {
        OneWay,

        TwoWay,

        OneWayToSource,

        OneTime
    }


    public interface IBindingMode
    {
        BindingMode Direction { get; }

        bool SupportsConversion { get; }

        bool SupportsValidation { get; }

        object? Convert(object? value, Type targetType);

        object? ConvertBack(object? value, Type sourceType);
    }


    public interface ICommandSource
    {
        string CommandName { get; }

        object? CommandParameter { get; }
    }


    public interface ICommandTarget
    {
        bool CanExecute(string commandName, object? parameter);

        void Execute(string commandName, object? parameter);
    }


    public class UICommand
    {
        public string CommandText { get; set; } = string.Empty;

        public Dictionary<string, object>? Parameters { get; set; }

        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }


    public interface IPipelineStage<TContext>
    {
        string Name { get; }

        string Description { get; }

        bool CanExecute(TContext context);

        Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
    }


    public interface IPipelineContext
    {
        string Mode { get; set; }

        IReadOnlyDictionary<string, bool> StageSuccess { get; }

        IReadOnlyDictionary<string, string?> StageErrors { get; }

        IDictionary<string, object?> SharedData { get; }

        void SetStageResult(string stageName, bool success, string? error = null);

        bool GetStageSuccess(string stageName);

        string? GetStageError(string stageName);

        bool HasError { get; }

        string? FirstError { get; }

        void Reset();
    }


    public interface IStagePipeline<TContext> where TContext : IPipelineContext
    {
        string Name { get; }

        string Description { get; }

        IStagePipeline<TContext> Register(IPipelineStage<TContext> stage);

        IStagePipeline<TContext> RegisterRange(IEnumerable<IPipelineStage<TContext>> stages);

        IStagePipeline<TContext> InsertBefore(string stageName, IPipelineStage<TContext> stage);

        IStagePipeline<TContext> InsertAfter(string stageName, IPipelineStage<TContext> stage);

        IStagePipeline<TContext> Remove(string stageName);

        Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);

        Task ExecuteForEachAsync(
            TContext context,
            IEnumerable<string> items,
            Action<TContext, string, int>? onItemBefore = null,
            Action<TContext, string, int, bool>? onItemAfter = null,
            CancellationToken cancellationToken = default);
    }
}
