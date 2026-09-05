
using System;
using System.Collections.Generic;
using System.Linq;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Utilities
{
    public class ContextSnapshot : IContextSnapshot
    {
        public string ContextId { get; }

        public DateTime SnapshotTime { get; }

        public IReadOnlyDictionary<string, object?> Data { get; }

        public ContextSnapshot(string contextId, IReadOnlyDictionary<string, object?> data)
        {
            ContextId = contextId;
            SnapshotTime = DateTime.UtcNow;
            Data = data;
        }

        public IReadOnlyList<SnapshotDiff> CompareTo(IContextSnapshot other)
        {
            var diffs = new List<SnapshotDiff>();
            var allKeys = Data.Keys.Union(other.Data.Keys).Distinct();

            foreach (var key in allKeys)
            {
                var hasOld = Data.TryGetValue(key, out var oldValue);
                var hasNew = other.Data.TryGetValue(key, out var newValue);

                if (!hasOld && hasNew)
                {
                    diffs.Add(new SnapshotDiff(key, null, newValue, DiffType.Added));
                }
                else if (hasOld && !hasNew)
                {
                    diffs.Add(new SnapshotDiff(key, oldValue, null, DiffType.Removed));
                }
                else if (hasOld && hasNew && !Equals(oldValue, newValue))
                {
                    diffs.Add(new SnapshotDiff(key, oldValue, newValue, DiffType.Modified));
                }
            }

            return diffs;
        }
    }

    public class SnapshotDiff
    {
        public string Key { get; }

        public object? OldValue { get; }

        public object? NewValue { get; }

        public DiffType Type { get; }

        public SnapshotDiff(string key, object? oldValue, object? newValue, DiffType type)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Type = type;
        }
    }

    public enum DiffType
    {
        Added,
        Removed,
        Modified
    }

    public abstract class SnapshotableContextBase : ISnapshotable
    {
        protected abstract string GetContextId();

        protected abstract IReadOnlyDictionary<string, object?> GetSnapshotData();

        protected abstract void RestoreFromSnapshotData(IReadOnlyDictionary<string, object?> data);

        public IContextSnapshot CreateSnapshot()
        {
            return new ContextSnapshot(GetContextId(), GetSnapshotData());
        }

        public void RestoreSnapshot(IContextSnapshot snapshot)
        {
            if (snapshot.ContextId != GetContextId())
                throw new InvalidOperationException($"快照上下文标识不匹配：期望 {GetContextId()}，实际 {snapshot.ContextId}");

            RestoreFromSnapshotData(snapshot.Data);
        }
    }
}
