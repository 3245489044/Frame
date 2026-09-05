
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace TConfig.ContextFrame.Abstractions
{
    public interface IContext : IDisposable, IAsyncDisposable
    {

        string Id { get; }

        IContext? Parent { get; }

        bool IsDisposed { get; }

        DateTime CreatedAt { get; }

        CancellationToken CancellationToken { get; }


        void SetData<T>(T value, string? key = null) where T : notnull;

        T? GetData<T>(string? key = null) where T : notnull;

        bool TryGetData<T>([MaybeNullWhen(false)] out T? value, string? key = null) where T : notnull;

        bool RemoveData<T>(string? key = null) where T : notnull;

        bool ContainsData<T>(string? key = null) where T : notnull;

        IEnumerable<T> GetAllData<T>() where T : notnull;

        IEnumerable<object> GetAllData();


        T GetOrAddService<T>(Func<T> factory) where T : notnull;

        bool TryGetService<T>([MaybeNullWhen(false)] out T? service) where T : notnull;


        event EventHandler<ContextDataChangedEventArgs>? DataChanged;
    }

    public class ContextDataChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public object? OldValue { get; }

        public object? NewValue { get; }

        public DataChangeType ChangeType { get; }

        public ContextDataChangedEventArgs(string key, object? oldValue, object? newValue, DataChangeType changeType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ChangeType = changeType;
        }
    }

    public enum DataChangeType
    {
        Added,
        Updated,
        Removed,
        Cleared
    }
}
