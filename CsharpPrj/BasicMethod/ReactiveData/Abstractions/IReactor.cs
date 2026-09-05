
#nullable enable

namespace Modules.ReactiveData.Abstractions
{
    public class DataChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public object? OldValue { get; }

        public object? NewValue { get; }

        public DateTime Timestamp { get; }

        public DataChangeType ChangeType { get; }

        public DataChangedEventArgs(string key, object? oldValue, object? newValue, DataChangeType changeType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ChangeType = changeType;
            Timestamp = DateTime.UtcNow;
        }
    }

    public enum DataChangeType
    {
        Added,
        Updated,
        Removed,
        Cleared
    }

    public interface IReactor<in T>
    {
        float Priority { get; }

        bool ShouldReact(T data);

        void React(T data);
    }

    public interface IReactor<in T, in TContext>
    {
        float Priority { get; }

        bool ShouldReact(T data);

        void React(T data, TContext context);
    }
}
