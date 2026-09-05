
using System.Collections.Concurrent;

namespace TConfig;

public enum ErrorLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Fatal = 3
}

public class ErrorEntry
{
    public string Code { get; }

    public string Message { get; }

    public ErrorLevel Level { get; }

    public DateTime Timestamp { get; }

    public Exception? Exception { get; }

    public ErrorEntry(string code, string message, ErrorLevel level = ErrorLevel.Error, Exception? exception = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Level = level;
        Timestamp = DateTime.UtcNow;
        Exception = exception;
    }

    public override string ToString()
        => $"[{Level}] {Code}: {Message}";
}

public class ErrorCollector
{
    private readonly ConcurrentBag<ErrorEntry> _errors = new();

    public IReadOnlyCollection<ErrorEntry> Errors => _errors.ToList().AsReadOnly();

    public bool HasErrors => !_errors.IsEmpty;

    public bool HasFatalErrors => FatalCount > 0;

    public bool HasRealErrors => FatalCount > 0 || ErrorCount > 0;

    public bool HasWarnings => WarningCount > 0;

    public bool HasInfos => InfoCount > 0;

    public int Count => _errors.Count;

    public int FatalCount => _errors.Count(e => e.Level == ErrorLevel.Fatal);

    public int ErrorCount => _errors.Count(e => e.Level == ErrorLevel.Error);

    public int WarningCount => _errors.Count(e => e.Level == ErrorLevel.Warning);

    public int InfoCount => _errors.Count(e => e.Level == ErrorLevel.Info);

    public void Add(string code, string message, ErrorLevel level = ErrorLevel.Error, Exception? exception = null)
    {
        _errors.Add(new ErrorEntry(code, message, level, exception));
    }

    public void Merge(ErrorCollector other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        foreach (var error in other._errors)
            _errors.Add(error);
    }

    public void Clear()
    {
        _errors.Clear();
    }

    public IEnumerable<string> GetErrorCodes()
        => _errors.Select(e => e.Code).Distinct();

    public IEnumerable<ErrorEntry> GetErrorsByLevel(ErrorLevel level)
        => _errors.Where(e => e.Level == level);

    public string GetErrorReport()
    {
        if (_errors.IsEmpty) return "No errors.";

        var lines = new List<string>
        {
            $"Error Report ({Count} entries):",
            $"  Fatal: {FatalCount}, Error: {ErrorCount}, Warning: {WarningCount}, Info: {InfoCount}",
            "---"
        };

        foreach (var error in _errors.OrderByDescending(e => e.Level).ThenBy(e => e.Timestamp))
            lines.Add($"  [{error.Level}] {error.Code}: {error.Message}");

        return string.Join(Environment.NewLine, lines);
    }
}
