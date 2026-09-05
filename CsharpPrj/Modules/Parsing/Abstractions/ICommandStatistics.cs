using Modules.Parsing.Models;

namespace Modules.Parsing.Abstractions;

public interface ICommandStatistics
{
    void RecordExecution(string verb, string? action, bool success, TimeSpan duration);
    void RecordError(string verb, ErrorCode errorCode);
    CommandMetrics GetMetrics();
    void Reset();
    void Start();
    void Stop();
}
