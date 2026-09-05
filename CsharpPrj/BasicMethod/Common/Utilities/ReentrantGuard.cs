
using System.Threading;

namespace Modules.Common.Utilities
{
    public struct ReentrantGuard
    {
        private int _guard;

        public bool TryEnter() => Interlocked.CompareExchange(ref _guard, 1, 0) == 0;

        public void Exit() => Interlocked.Exchange(ref _guard, 0);
    }
}
