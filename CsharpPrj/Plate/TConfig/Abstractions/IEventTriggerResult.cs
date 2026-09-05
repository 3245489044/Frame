
namespace TConfig;

public interface IEventTriggerResult
{
    bool IsSuccess { get; }

    string? ErrorMessage { get; }

    object? Value { get; }
}
