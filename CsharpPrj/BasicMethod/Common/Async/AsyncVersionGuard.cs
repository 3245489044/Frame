
#nullable enable

namespace Modules.Common.Async
{
    public sealed class AsyncVersionGuard
    {
        private int _version;

        public int Current => Volatile.Read(ref _version);

        public int Next()
        {
            return Interlocked.Increment(ref _version);
        }

        public bool IsCurrent(int expected)
        {
            return Current == expected;
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _version, 0);
        }

        public async Task ExecuteAsync<TResult>(
            Func<Task<TResult>> asyncFunc,
            Action<TResult> onCompleted,
            Action? onStale = null)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            var version = Next();
            var result = await asyncFunc().ConfigureAwait(false);

            if (IsCurrent(version))
            {
                onCompleted(result);
            }
            else
            {
                onStale?.Invoke();
            }
        }

        public async Task ExecuteAsync(
            Func<Task> asyncFunc,
            Action onCompleted,
            Action? onStale = null)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            var version = Next();
            await asyncFunc().ConfigureAwait(false);

            if (IsCurrent(version))
            {
                onCompleted();
            }
            else
            {
                onStale?.Invoke();
            }
        }
    }
}
