
namespace TConfig.ContextFrame.Abstractions;

public class StorageEntry
{
    public object? Value { get; set; }

    public StorageLayer Layer { get; init; }

    public SnapshotBehavior SnapshotBehavior { get; init; }

    public DateTime CreatedAt { get; init; }

    public TimeSpan? Ttl { get; init; }

    public StorageEntry(
        object? value,
        StorageLayer layer,
        SnapshotBehavior? snapshotBehavior = null,
        TimeSpan? ttl = null)
    {
        Value = value;
        Layer = layer;
        SnapshotBehavior = snapshotBehavior ?? GetDefaultBehavior(layer);
        CreatedAt = DateTime.UtcNow;
        Ttl = ttl;
    }

    private static SnapshotBehavior GetDefaultBehavior(StorageLayer layer) => layer switch
    {
        StorageLayer.Config => SnapshotBehavior.Preserve,
        StorageLayer.Session => SnapshotBehavior.Snapshot,
        StorageLayer.Transient => SnapshotBehavior.Exclude,
        _ => SnapshotBehavior.Snapshot
    };

    public bool IsExpired =>
        Ttl.HasValue && DateTime.UtcNow - CreatedAt > Ttl.Value;
}
