
#nullable enable

namespace Modules.ComponentData.Abstractions
{
    public class ComponentDataChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public object? OldValue { get; }

        public object? NewValue { get; }

        public DateTime Timestamp { get; }

        public ComponentDataChangeType ChangeType { get; }

        public ComponentDataChangedEventArgs(string key, object? oldValue, object? newValue, ComponentDataChangeType changeType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ChangeType = changeType;
            Timestamp = DateTime.UtcNow;
        }
    }

    public enum ComponentDataChangeType
    {
        Added,
        Updated,
        Removed,
        Cleared
    }
}
