
namespace Modules.InternalStorage.Initialization;

public record InitializerResult(
    string Name,
    bool Success,
    Exception? Error,
    TimeSpan Duration)
{
    public InitializerResult(string name, TimeSpan duration)
        : this(name, true, null, duration) { }

    public InitializerResult(string name, Exception error, TimeSpan duration)
        : this(name, false, error, duration) { }
}

public record InitializationError(
    string InitializerName,
    string Message,
    Exception? InnerException);

public class InitializationResult
{
    private readonly List<InitializerResult> _results;

    public IReadOnlyList<InitializerResult> Results => _results.AsReadOnly();

    public bool Success => _results.All(r => r.Success);

    public IReadOnlyList<InitializerResult> FailedResults =>
        _results.Where(r => !r.Success).ToList().AsReadOnly();

    public IReadOnlyList<InitializationError> Errors =>
        _results
            .Where(r => !r.Success && r.Error != null)
            .Select(r => new InitializationError(
                r.Name,
                r.Error!.Message,
                r.Error.InnerException))
            .ToList()
            .AsReadOnly();

    public int SuccessCount => _results.Count(r => r.Success);

    public int FailureCount => _results.Count(r => !r.Success);

    public TimeSpan TotalDuration
    {
        get
        {
            if (_results.Count == 0) return TimeSpan.Zero;
            return _results.Max(r => r.Duration);
        }
    }

    public InitializationResult(IEnumerable<InitializerResult> results)
    {
        _results = results?.ToList() ?? throw new ArgumentNullException(nameof(results));
    }

    public static InitializationResult Empty { get; } =
        new InitializationResult(Array.Empty<InitializerResult>());
}
