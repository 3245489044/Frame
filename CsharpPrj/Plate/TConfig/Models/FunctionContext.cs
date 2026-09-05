
#nullable enable

namespace TConfig
{
    public class FunctionContext
    {
        public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");


        public string FunctionName { get; set; } = string.Empty;

        public Dictionary<string, object?> Parameters { get; set; } = new();


        public int? SourceEntityId { get; set; }


        public bool IsSuccess { get; set; }

        public object? ResultValue { get; set; }

        public string? ErrorMessage { get; set; }


        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        public IServiceProvider? ServiceProvider { get; set; }

        public FunctionContext? ParentContext { get; set; }

        public List<string> Tags { get; set; } = new();

        public Dictionary<string, object> Items { get; set; } = new();


        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public DateTime? CompletedTimestamp { get; set; }

        public void AddTag(string tag)
        {
            if (!Tags.Contains(tag))
                Tags.Add(tag);
        }

        public bool HasTag(string tag) => Tags.Contains(tag);

        public FunctionContext CreateSubContext(string? functionName = null)
        {
            return new FunctionContext
            {
                ExecutionId = ExecutionId,
                FunctionName = functionName ?? FunctionName,
                CancellationToken = CancellationToken,
                ServiceProvider = ServiceProvider,
                ParentContext = this,
                Parameters = new Dictionary<string, object?>(Parameters),
                Tags = new List<string>(Tags),
                Items = new Dictionary<string, object>(Items)
            };
        }
    }
}