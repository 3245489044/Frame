
using System.Collections.Concurrent;

namespace Common.Diagnostics
{
    public class CollapserConfig
    {
        public bool Enabled { get; set; } = true;

        public int Threshold { get; set; } = 3;

        public int FlushTimeoutMs { get; set; } = 5000;

        public int MaxCollapsedLines { get; set; } = 5;

        public string SummaryFormat { get; set; } = "[折叠] {0} (x{1})";
    }

    public static class LogCollapser
    {
        private static readonly ConcurrentDictionary<string, CollapseState> _states = new();
        private static readonly object _flushLock = new();
        private static CollapserConfig _config = new();
        private static System.Threading.Timer? _flushTimer;

        private static volatile bool _timerStarted;

        public static CollapserConfig Config
        {
            get => _config;
            set => _config = value ?? new CollapserConfig();
        }

        public static bool Write(string message)
        {
            if (!_config.Enabled)
            {
                Console.WriteLine(message);
                return true;
            }

            var key = NormalizeMessage(message);

            lock (_flushLock)
            {
                if (_states.TryGetValue(key, out var state))
                {
                    var count = state.Increment();

                    if (count < _config.Threshold)
                    {
                        Console.WriteLine(message);
                        return true;
                    }
                    else if (count == _config.Threshold)
                    {
                        Console.WriteLine(_config.SummaryFormat, state.OriginalMessage, count);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    var newState = new CollapseState(message);
                    _states[key] = newState;
                    Console.WriteLine(message);

                    EnsureTimerStarted();

                    return true;
                }
            }
        }

        public static void Flush()
        {
            lock (_flushLock)
            {
                foreach (var kvp in _states)
                {
                    var count = kvp.Value.Count;
                    if (count > _config.Threshold)
                    {
                        Console.WriteLine(_config.SummaryFormat, kvp.Value.OriginalMessage, count);
                    }
                }
                _states.Clear();
            }
        }

        public static void Reset()
        {
            lock (_flushLock)
            {
                _states.Clear();
                _flushTimer?.Dispose();
                _flushTimer = null;
                _timerStarted = false;
            }
        }

        public static int ActiveCount => _states.Count;

        private static string NormalizeMessage(string message)
        {
            var idx = message.LastIndexOf("] ");
            if (idx >= 0 && idx < message.Length - 2)
            {
                return message[(idx + 2)..].Trim();
            }
            return message.Trim();
        }

        private static void EnsureTimerStarted()
        {
            if (_timerStarted) return;

            lock (_flushLock)
            {
                if (_timerStarted) return;
                _timerStarted = true;

                _flushTimer = new System.Threading.Timer(
                    _ => Flush(),
                    null,
                    _config.FlushTimeoutMs,
                    _config.FlushTimeoutMs);

            }
        }

        private class CollapseState
        {
            public string OriginalMessage { get; }

            public int Count { get; private set; }

            public CollapseState(string originalMessage)
            {
                OriginalMessage = originalMessage;
                Count = 1;
            }

            public int Increment()
            {
                Count++;
                return Count;
            }
        }
    }
}
