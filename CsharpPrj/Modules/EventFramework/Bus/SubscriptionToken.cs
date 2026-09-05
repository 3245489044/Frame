
namespace Modules.EventFramework.Bus;

public class SubscriptionToken : TConfig.IEventSubscription
{
    private readonly Action _unsubscribe;
    private bool _disposed;
    private readonly object _lock = new();

    public DateTime CreatedAt { get; }

    public bool IsDisposed => _disposed;

    internal SubscriptionToken(Action unsubscribe)
    {
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        CreatedAt = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _unsubscribe?.Invoke();
                }
            }
        }
    }
}

public class SubscriptionManager : IDisposable
{
    private readonly List<SubscriptionToken> _tokens = new();
    private readonly object _lock = new();
    private bool _disposed;

    public int Count
    {
        get { lock (_lock) return _tokens.Count; }
    }

    public SubscriptionToken Add(SubscriptionToken token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _tokens.Add(token);
        }
        return token;
    }

    public void UnsubscribeAll()
    {
        lock (_lock)
        {
            foreach (var token in _tokens)
            {
                if (!token.IsDisposed)
                    token.Dispose();
            }
            _tokens.Clear();
        }
    }

    public bool Remove(SubscriptionToken token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        lock (_lock)
        {
            if (_tokens.Remove(token))
            {
                if (!token.IsDisposed)
                    token.Dispose();
                return true;
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            UnsubscribeAll();
        }
    }
}
