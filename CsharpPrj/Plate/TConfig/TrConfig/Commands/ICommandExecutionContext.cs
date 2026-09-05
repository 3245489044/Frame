
namespace TConfig;

public interface ICommandExecutionContext
{
    string CommandName { get; }

    string CommandArgs { get; }

    Dictionary<string, object?> Parameters { get; }

    CancellationToken CancellationToken { get; }

    IServiceProvider? ServiceProvider { get; }

    T? GetParameter<T>(string name);

    void SetParameter(string name, object? value);
}
