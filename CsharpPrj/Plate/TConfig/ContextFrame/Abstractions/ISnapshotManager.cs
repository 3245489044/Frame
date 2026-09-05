
namespace TConfig.ContextFrame.Abstractions;

public interface ISnapshotManager
{
    ILayeredSnapshot CreateSnapshot(IStorageCell cell);

    void RestoreSnapshot(IStorageCell cell, ILayeredSnapshot snapshot);

    IReadOnlyList<ISnapshotDiff> CalculateDiff(ILayeredSnapshot before, ILayeredSnapshot after);
}

public interface ISnapshotDiff
{
    string Key { get; }

    object? OldValue { get; }

    object? NewValue { get; }

    DiffType Type { get; }
}

public enum DiffType
{
    Added,
    Removed,
    Modified
}
