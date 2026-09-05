
namespace TConfig;

public interface IExecutionContextLegacy
{
    string Id { get; }

    void SetData(string key, object? value);

    T? GetData<T>(string key) where T : class;

    bool ContainsKey(string key);

    CancellationToken CancellationToken { get; }
}

public class ExecutionContext : IExecutionContext
{
    private readonly Dictionary<string, object?> _data = new();

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string ExecutionId => Id;

    public string WorkflowId { get; set; } = string.Empty;

    public string NodeKey { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

    public IDictionary<string, object> Items { get; set; } = new Dictionary<string, object>();

    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    public IServiceProvider? ServiceProvider { get; set; }

    public T GetParameter<T>(string key)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typed)
            return typed;
        throw new KeyNotFoundException($"Parameter '{key}' not found.");
    }

    public T GetParameter<T>(int index)
    {
        if (index < 0 || index >= Parameters.Count)
            throw new IndexOutOfRangeException($"Parameter index {index} out of range.");
        var value = Parameters.Values.ElementAt(index);
        if (value is T typed)
            return typed;
        throw new InvalidCastException($"Parameter at index {index} cannot be cast to {typeof(T).Name}.");
    }

    public bool TryGetParameter<T>(string key, out T value)
    {
        if (Parameters.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    public T GetData<T>(string key)
    {
        if (Items.TryGetValue(key, out var value) && value is T typed)
            return typed;
        if (_data.TryGetValue(key, out var oldValue) && oldValue is T typedOld)
            return typedOld;
        throw new KeyNotFoundException($"Data '{key}' not found.");
    }

    public bool TryGetData<T>(string key, out T value)
    {
        if (Items.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        if (_data.TryGetValue(key, out var oldObj) && oldObj is T typedOld)
        {
            value = typedOld;
            return true;
        }
        value = default!;
        return false;
    }

    public void SetData<T>(string key, T value)
    {
        Items[key] = value!;
        _data[key] = value;
    }

    public void SetItem(string key, object? value)
    {
        Items[key] = value!;
        _data[key] = value;
    }

    public IExecutionContext CreateSubContext(string? nodeKey = null)
    {
        return new ExecutionContext
        {
            WorkflowId = WorkflowId,
            NodeKey = nodeKey ?? NodeKey,
            Parameters = new Dictionary<string, object>(Parameters),
            Items = new Dictionary<string, object>(Items),
            CancellationToken = CancellationToken,
            ServiceProvider = ServiceProvider
        };
    }


    public bool ContainsKey(string key) => Items.ContainsKey(key) || _data.ContainsKey(key);
}


    public class ExecutionResult
    {
        public bool IsSuccess { get; set; }

        public object? Value { get; set; }

        public string? ErrorMessage { get; set; }

        public Exception? Exception { get; set; }

        public TimeSpan ExecutionTime { get; set; }

        public TimeSpan Duration
        {
            get => ExecutionTime;
            set => ExecutionTime = value;
        }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public Dictionary<string, object?>? Outputs { get; set; }

        public Dictionary<string, object?>? Metadata { get; set; }

        public List<StepResult>? StepResults { get; set; }

        public bool IsOk => IsSuccess;

        public bool IsAccepted { get; internal set; }

        public bool IsCancelled => ErrorMessage == "CANCELLED";

        public bool IsTimedOut => ErrorMessage == "TIMEOUT";

        public ExecutionStatus Status { get; set; }

        public string? Message
        {
            get => ErrorMessage;
            set => ErrorMessage = value;
        }

        public static ExecutionResult Success(object? value = null)
            => new() { IsSuccess = true, Value = value };

        public static ExecutionResult Success(object? value, TimeSpan executionTime)
            => new() { IsSuccess = true, Value = value, ExecutionTime = executionTime };

        public static ExecutionResult Ok(object? value = null)
            => new() { IsSuccess = true, Value = value };

        public static ExecutionResult Fail(string errorMessage)
            => new() { IsSuccess = false, ErrorMessage = errorMessage };

        public static ExecutionResult Failure(string errorMessage, Exception? exception = null)
            => new() { IsSuccess = false, ErrorMessage = errorMessage, Exception = exception };

        public static ExecutionResult Succeed(object? value = null, TimeSpan? executionTime = null)
            => new() { IsSuccess = true, Value = value, ExecutionTime = executionTime ?? TimeSpan.Zero };

        public static ExecutionResult Cancelled()
            => new() { IsSuccess = false, ErrorMessage = "CANCELLED" };

        public static ExecutionResult Timeout()
            => new() { IsSuccess = false, ErrorMessage = "TIMEOUT" };

        public static ExecutionResult Accepted(string functionName, Dictionary<string, object>? parameters = null)
            => new()
            {
                IsSuccess = false,
                IsAccepted = true,
                ErrorMessage = "ACCEPTED",
                Metadata = new Dictionary<string, object?>
                {
                    ["functionName"] = functionName,
                    ["parameters"] = parameters,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
    }

public class StepResult
{
    public string StepId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public object? Value { get; set; }

    public string? ErrorMessage { get; set; }
}

public enum ExecutionStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed,
    Timeout,
    TimedOut
}