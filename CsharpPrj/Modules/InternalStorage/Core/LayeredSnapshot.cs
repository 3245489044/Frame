
using TConfig.ContextFrame.Abstractions;
namespace Modules.InternalStorage.Core;

public class LayeredSnapshot : ILayeredSnapshot
{
    public string Id { get; }

    public DateTime CreatedAt { get; }

    public IReadOnlyDictionary<string, object?> SessionData { get; }

    public LayeredSnapshot(string id, IReadOnlyDictionary<string, object?> sessionData)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
        SessionData = new Dictionary<string, object?>(sessionData);
    }

    public IContextSnapshot ToContextSnapshot()
    {
        return new ContextSnapshotAdapter(Id, CreatedAt, SessionData);
    }

    private class ContextSnapshotAdapter : IContextSnapshot
    {
        public string ContextId { get; }
        public DateTime SnapshotTime { get; }
        public IReadOnlyDictionary<string, object?> Data { get; }

        public ContextSnapshotAdapter(string contextId, DateTime snapshotTime, IReadOnlyDictionary<string, object?> data)
        {
            ContextId = contextId;
            SnapshotTime = snapshotTime;
            Data = data;
        }
    }
}
