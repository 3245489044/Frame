
using System.Text.Json.Serialization;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Parsing;


namespace CommandFramework.Abstractions
{

    public interface ICommandEngine : IDisposable
    {
        void RegisterCommand(string name, Func<CommandExecutionContext, CancellationToken, Task> execute,
            Func<CommandExecutionContext, CancellationToken, Task>? undo = null, string? description = null);

        void RegisterCommand(ICommand command);

        Task<bool> ExecuteAsync(string name, CommandExecutionContext? context = null,
            CancellationToken ct = default);

        Task<bool> UndoAsync(CancellationToken ct = default);

        Task<bool> RedoAsync(CancellationToken ct = default);

        bool CanUndo { get; }

        bool CanRedo { get; }

        void ClearHistory();

        string[] GetRegisteredCommands();

    }



    public interface ICommand
    {
        string Name { get; }

        string? Description { get; }

        bool CanUndo { get; }

        Task ExecuteAsync(CancellationToken ct = default);

        Task UndoAsync(CancellationToken ct = default);
    }


    public interface ICommandExecutionResult
    {
        bool IsSuccess { get; }

        string Command { get; }

        string? Value { get; }

        string? Error { get; }

        string? ErrorCode { get; }

        long ExecutionTimeMs { get; }

        DateTime Timestamp { get; }

        string ToJson();
    }

    public interface ICommandBatchResult
    {
        int TotalCount { get; }

        int SuccessCount { get; }

        int FailCount { get; }

        bool IsAllSuccess { get; }

        string? Error { get; }

        IReadOnlyList<ICommandExecutionResult> Results { get; }
    }


    public interface ICommandParser
    {
        ParsedCommand Parse(string command);

        string? Normalize(string command);

        bool IsNonEventCommand(string verb, string? action);
    }


    public class BatchResult : ICommandBatchResult
    {
        public int TotalCount { get; set; }

        public int SuccessCount { get; set; }

        public int FailCount { get; set; }

        public bool IsAllSuccess => FailCount == 0;

        public string? Error { get; set; }

        private List<ICommandExecutionResult> _results = new();
        public IReadOnlyList<ICommandExecutionResult> Results => _results;

        public void AddResult(ICommandExecutionResult result)
        {
            _results.Add(result);
            TotalCount = _results.Count;
            if (result.IsSuccess) SuccessCount++; else FailCount++;
        }
    }


    public interface ICommandPipeline<TContext>
    {
        string Name { get; }

        string Description { get; }

        ICommandPipeline<TContext> Register(IPipelineStage<TContext> stage);

        ICommandPipeline<TContext> RegisterRange(IEnumerable<IPipelineStage<TContext>> stages);

        ICommandPipeline<TContext> InsertBefore(string stageName, IPipelineStage<TContext> stage);

        ICommandPipeline<TContext> InsertAfter(string stageName, IPipelineStage<TContext> stage);

        ICommandPipeline<TContext> Remove(string stageName);

        Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);

        Task ExecuteForEachAsync(
            TContext context,
            IEnumerable<string> items,
            Action<TContext, string, int>? onItemBefore = null,
            Action<TContext, string, int, bool>? onItemAfter = null,
            CancellationToken cancellationToken = default);
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


    public interface IPipelineStage<TContext>
    {
        string Name { get; }

        string Description { get; }

        bool CanExecute(TContext context);

        Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
    }



    public interface IEnvironmentAdapter
    {
        string Name { get; }

        Task<ICommandExecutionResult> DoAsync(string target, string value);

        Task<ICommandExecutionResult> GetAsync(string target);

        Task<ICommandExecutionResult> LookAsync(string target);

        Task<ICommandExecutionResult> WaitAsync(string target, string? condition = null);

        Task<ICommandExecutionResult> AssertAsync(string target, string? expectedValue = null);

        Task<ICommandExecutionResult> DiscoverAsync(string? filter = null);
    }


    public interface INonEventCommandHandler
    {
        Task<CommandExecutionResult<object?>> HandleAsync(ParsedCommand command);

        void RegisterHandler(string actionName, Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler);

        bool CanHandle(string actionName);
    }



    public interface IStateStore
    {
        void Set(string key, string? value);

        string? Get(string key);

        bool Has(string key);

        string? GetOrAdd(string key, string? defaultValue);

        void Clear();

        IReadOnlyCollection<string> GetAllKeys();

        int Count { get; }

        void Remove(string key);

        void RemoveByPrefix(string prefix);
    }


}
