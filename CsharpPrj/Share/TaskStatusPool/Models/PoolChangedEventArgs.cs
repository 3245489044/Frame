
#nullable enable

namespace Share.TaskStatusPool.Models
{
    public class PoolChangedEventArgs<T> : EventArgs
    {
        public PoolChangeType ChangeType { get; }

        public T? OldValue { get; }

        public T? NewValue { get; }

        public PoolChangedEventArgs(PoolChangeType changeType, T? oldValue = default, T? newValue = default)
        {
            ChangeType = changeType;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public override string ToString()
            => $"[{ChangeType}] Old={OldValue?.ToString() ?? "null"}, New={NewValue?.ToString() ?? "null"}";
    }

    public enum PoolChangeType
    {
        Added,

        Updated,

        Removed,

        Cleared
    }
}
