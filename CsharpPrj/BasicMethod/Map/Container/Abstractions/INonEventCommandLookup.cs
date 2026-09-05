
namespace Container.Abstractions;

public interface INonEventCommandLookup
{
    bool TryGetHandler(string commandName, out Action<TConfig.IExecutionContext>? handler);

    IReadOnlyCollection<string> RegisteredCommandNames { get; }

    bool Contains(string commandName);
}
