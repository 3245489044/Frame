using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.StateMachine
{
    #region 暂停管理器

    public class PauseManager : IPauseManager
    {
        private int _globalPauseFlag;
        private readonly ConcurrentDictionary<string, int> _localPauseFlags = new(StringComparer.OrdinalIgnoreCase);

        public bool IsGloballyPaused => Interlocked.CompareExchange(ref _globalPauseFlag, 0, 0) == 1;

        public void RequestGlobalPause()
        {
            Interlocked.Exchange(ref _globalPauseFlag, 1);
        }

        public void ResumeGlobalPause()
        {
            Interlocked.Exchange(ref _globalPauseFlag, 0);
        }

        public bool IsLocallyPaused(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return false;

            return _localPauseFlags.TryGetValue(scopeId, out var flag) && flag == 1;
        }

        public void RequestLocalPause(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                throw new ArgumentException("Scope ID cannot be empty", nameof(scopeId));

            _localPauseFlags[scopeId] = 1;
        }

        public void ResumeLocalPause(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                throw new ArgumentException("Scope ID cannot be empty", nameof(scopeId));

            _localPauseFlags.TryRemove(scopeId, out _);
        }

        public async Task WaitForResumeAsync(string? scopeId, CancellationToken cancellationToken, int pollIntervalMs = 100)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool isPaused;

                if (scopeId == null)
                {
                    isPaused = IsGloballyPaused;
                }
                else
                {
                    isPaused = IsGloballyPaused || IsLocallyPaused(scopeId);
                }

                if (!isPaused)
                    return;

                await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    #endregion
}
