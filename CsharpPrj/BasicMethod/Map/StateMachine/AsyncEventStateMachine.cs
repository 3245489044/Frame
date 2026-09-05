using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.StateMachine
{
    #region 异步事件状态机

    public class AsyncEventStateMachine<TEvent, TResult> : IStateMachine<AsyncEventStateMachine<TEvent, TResult>.State>
    {
        public enum State
        {
            Idle,
            Waiting,
            Completed,
            TimedOut,
            Canceled
        }

        private readonly object _lock = new();
        private TaskCompletionSource<TResult>? _tcs;
        private Func<TEvent, bool>? _condition;
        private Timer? _timer;
        private CancellationTokenRegistration _cancellationRegistration;

        public State CurrentState { get; private set; } = State.Idle;

        public event EventHandler<StateChangedEventArgs<State>>? StateChanged
        {
            add => _emitter.Subscribe(value);
            remove => _emitter.Unsubscribe(value);
        }

        private readonly StateChangeEmitter<State> _emitter = new();

        public Task<TResult> WaitAsync(
            Func<TEvent, bool> condition,
            int timeoutMs = Timeout.Infinite,
            CancellationToken cancellationToken = default)
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            lock (_lock)
            {
                if (CurrentState == State.Waiting)
                    throw new InvalidOperationException("状态机已在等待中，不能重复调用 WaitAsync。");

                SetState(State.Waiting);

                _condition = condition;
                _tcs = new TaskCompletionSource<TResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                if (timeoutMs != Timeout.Infinite && timeoutMs > 0)
                {
                    _timer = new Timer(OnTimeout, null, timeoutMs, Timeout.Infinite);
                }

                if (cancellationToken.CanBeCanceled)
                {
                    _cancellationRegistration = cancellationToken.Register(OnCanceled);
                }

                return _tcs.Task;
            }
        }

        public bool OnEvent(TEvent eventArgs)
        {
            TaskCompletionSource<TResult>? tcs = null;
            bool matched = false;

            lock (_lock)
            {
                if (CurrentState != State.Waiting || _condition == null || _tcs == null)
                    return false;

                if (!_condition(eventArgs))
                    return false;

                tcs = _tcs;
                Cleanup();
                matched = true;
            }

            if (matched)
            {
                SetState(State.Completed);
                TResult result = ConvertEventToResult(eventArgs);
                tcs?.TrySetResult(result);
            }

            return matched;
        }

        public void Reset()
        {
            lock (_lock)
            {
                Cleanup();
                SetState(State.Idle);
            }
        }

        void IStateMachine<State>.Reset(State initialState)
        {
            Reset();
        }

        protected virtual TResult ConvertEventToResult(TEvent eventArgs)
        {
            return default!;
        }

        #region 内部方法

        private void SetState(State newState)
        {
            var oldState = CurrentState;
            CurrentState = newState;
            _emitter.Raise(this, oldState, newState);
        }

        private void Cleanup()
        {
            _timer?.Dispose();
            _timer = null;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
            _condition = null;
            _tcs = null;
        }

        private void OnTimeout(object? state)
        {
            TaskCompletionSource<TResult>? tcs;

            lock (_lock)
            {
                if (CurrentState != State.Waiting)
                    return;

                tcs = _tcs;
                Cleanup();
                SetState(State.TimedOut);
            }

            tcs?.TrySetException(new TimeoutException("等待事件超时"));
        }

        private void OnCanceled()
        {
            TaskCompletionSource<TResult>? tcs;

            lock (_lock)
            {
                if (CurrentState != State.Waiting)
                    return;

                tcs = _tcs;
                Cleanup();
                SetState(State.Canceled);
            }

            tcs?.TrySetCanceled();
        }

        #endregion
    }

    #endregion
}
