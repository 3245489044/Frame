
namespace Container.Abstractions;

public interface IRuntimeContext
{
    object? GetService(Type serviceType);

    T? GetService<T>() where T : class;

    IDictionary<string, object?> Items { get; }
}
