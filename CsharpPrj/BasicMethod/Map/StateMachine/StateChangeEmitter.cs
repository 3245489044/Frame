
#nullable enable
using System;
using System.Threading;

namespace Modules.StateMachine
{
    public sealed class StateChangeEmitter<TState>
    {
        private event EventHandler<StateChangedEventArgs<TState>>? StateChanged;
        private readonly object _lock = new();

        public void Subscribe(EventHandler<StateChangedEventArgs<TState>> handler)
        {
            lock (_lock) StateChanged += handler;
        }

        public void Unsubscribe(EventHandler<StateChangedEventArgs<TState>> handler)
        {
            lock (_lock) StateChanged -= handler;
        }

        public void Raise(object sender, TState fromState, TState toState, string? operation = null)
        {
            EventHandler<StateChangedEventArgs<TState>>? handlers;
            lock (_lock)
            {
                handlers = StateChanged;
            }
            handlers?.Invoke(sender, new StateChangedEventArgs<TState>(fromState, toState, operation));
        }
    }
}
