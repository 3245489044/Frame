
using Modules.EventFramework.Core;

namespace Modules.EventFramework.Adapters;

public class TimerEventSourceNode : EventSourceNodeBase
{
    private readonly System.Timers.Timer _timer;
    private int _subscriptionCount;

    public override string Name { get; }

    public override string SourceType => "Timer";

    public override bool CanSubscribe => true;

    public override bool CanRead => false;

    public override bool CanWrite => false;

    public TimerEventSourceNode(string name, double intervalMs)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _timer = new System.Timers.Timer(intervalMs);
    }

    public TimerEventSourceNode(string name, System.Timers.Timer timer)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
    }

    protected override bool OnSubscribe(string eventName, Delegate handler)
    {
        if (string.Equals(eventName, "Elapsed", StringComparison.OrdinalIgnoreCase))
        {
            _timer.Elapsed += (System.Timers.ElapsedEventHandler)handler;
            Interlocked.Increment(ref _subscriptionCount);
            _timer.Start();
            return true;
        }
        return false;
    }

    protected override bool OnUnsubscribe(string eventName, Delegate handler)
    {
        if (string.Equals(eventName, "Elapsed", StringComparison.OrdinalIgnoreCase))
        {
            _timer.Elapsed -= (System.Timers.ElapsedEventHandler)handler;
            if (Interlocked.Decrement(ref _subscriptionCount) <= 0)
                _timer.Stop();
            return true;
        }
        return false;
    }

    protected override bool OnUnsubscribeAll(string eventName)
    {
        if (string.Equals(eventName, "Elapsed", StringComparison.OrdinalIgnoreCase))
        {
            _subscriptionCount = 0;
            _timer.Stop();
            return true;
        }
        return false;
    }

    protected override void OnDispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
