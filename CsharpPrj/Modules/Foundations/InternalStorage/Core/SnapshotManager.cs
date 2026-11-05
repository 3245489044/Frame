
using TConfig.ContextFrame.Abstractions;
using System.Linq;

namespace Modules.InternalStorage.Core;

public class SnapshotManager : ISnapshotManager
{
    public ILayeredSnapshot CreateSnapshot(IStorageCell cell)
    {
        var sessionData = new Dictionary<string, object?>();

        foreach (var kvp in cell.GetEntries(StorageLayer.Session))
        {
            var behavior = cell.GetSnapshotBehavior(kvp.Key, StorageLayer.Session);
            if (behavior == (int)SnapshotBehavior.Snapshot)
            {
                sessionData[kvp.Key] = kvp.Value;
            }
        }

        return new LayeredSnapshot(
            Guid.NewGuid().ToString("N"),
            sessionData);
    }

    public void RestoreSnapshot(IStorageCell cell, ILayeredSnapshot snapshot)
    {
        var sessionBehaviors = new Dictionary<string, int>();
        foreach (var entry in cell.GetEntries(StorageLayer.Session))
        {
            var behavior = cell.GetSnapshotBehavior(entry.Key, StorageLayer.Session);
            if (behavior.HasValue)
            {
                sessionBehaviors[entry.Key] = behavior.Value;
            }
        }

        foreach (var kvp in snapshot.SessionData)
        {
            if (sessionBehaviors.TryGetValue(kvp.Key, out var behavior))
            {
                if (behavior == (int)SnapshotBehavior.Preserve)
                {
                    continue;
                }
                cell.SetObject(kvp.Key, kvp.Value, StorageLayer.Session);
            }
        }
    }

    public IReadOnlyList<ISnapshotDiff> CalculateDiff(
        ILayeredSnapshot before,
        ILayeredSnapshot after)
    {
        var diffs = new List<ISnapshotDiff>();
        var allKeys = before.SessionData.Keys
            .Union(after.SessionData.Keys)
            .Distinct()
            .ToList();

        foreach (var key in allKeys)
        {
            var hasBefore = before.SessionData.TryGetValue(key, out var beforeValue);
            var hasAfter = after.SessionData.TryGetValue(key, out var afterValue);

            if (!hasBefore && hasAfter)
            {
                diffs.Add(new SnapshotDiffItem(key, null, afterValue, DiffType.Added));
            }
            else if (hasBefore && !hasAfter)
            {
                diffs.Add(new SnapshotDiffItem(key, beforeValue, null, DiffType.Removed));
            }
            else if (hasBefore && hasAfter && !Equals(beforeValue, afterValue))
            {
                diffs.Add(new SnapshotDiffItem(key, beforeValue, afterValue, DiffType.Modified));
            }
        }

        return diffs;
    }

    private class SnapshotDiffItem : ISnapshotDiff
    {
        public string Key { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }
        public DiffType Type { get; }

        public SnapshotDiffItem(string key, object? oldValue, object? newValue, DiffType type)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Type = type;
        }
    }
}
