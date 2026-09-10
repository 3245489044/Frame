
namespace Modules.StateManagement
{
    public class StateChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public string? OldValue { get; }

        public string? NewValue { get; }

        public StateChangeType ChangeType { get; }

        public StateChangedEventArgs(string key, string? oldValue, string? newValue, StateChangeType changeType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ChangeType = changeType;
        }
    }

    public enum StateChangeType
    {
        Set,
        Remove,
        RemoveByPrefix,
        Clear
    }
}
