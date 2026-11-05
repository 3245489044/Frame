
namespace Modules.DiagnosticContext.Abstractions;

public interface IDiagnosticContext : IDisposable
{
    string Name { get; }

    DateTime CreatedAt { get; }

    void Record(string eventName, object? data = null);

    void SetState(string key, object? value);

    T? GetState<T>(string key);

    IDiagnosticContext CreateScope(string scopeName);

    string ToString();

    string ToSummary();

    string ToJson();
}
