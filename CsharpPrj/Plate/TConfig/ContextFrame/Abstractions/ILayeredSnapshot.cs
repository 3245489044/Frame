
namespace TConfig.ContextFrame.Abstractions;

public interface ILayeredSnapshot
{
    string Id { get; }

    DateTime CreatedAt { get; }

    IReadOnlyDictionary<string, object?> SessionData { get; }

    IContextSnapshot ToContextSnapshot();
}
