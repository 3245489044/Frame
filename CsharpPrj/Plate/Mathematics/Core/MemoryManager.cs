using System;

namespace MathematicalAlgorithms
{
    public static class MemoryManager
    {
        private static long _lastCheckTime;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        public static bool HasSufficientMemory(long requiredBytes, object config = null)
        {
            if (requiredBytes <= 0) return true;

            try
            {
                var currentTime = Environment.TickCount64;
                if (currentTime - _lastCheckTime < CheckInterval.TotalMilliseconds)
                    return true;

                _lastCheckTime = currentTime;

                var process = System.Diagnostics.Process.GetCurrentProcess();
                var availableMemory = GetAvailableMemory();

                return availableMemory > requiredBytes * 1.5;
            }
            catch
            {
                return true;
            }
        }

        private static long GetAvailableMemory()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var totalMemory = process.WorkingSet64;
                var memoryLimit = GetMemoryLimit();

                return memoryLimit > 0 ? memoryLimit - totalMemory : long.MaxValue;
            }
            catch
            {
                return long.MaxValue;
            }
        }

        private static long GetMemoryLimit()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.MaxWorkingSet.ToInt64();
            }
            catch
            {
                return long.MaxValue;
            }
        }

        public static long GetCurrentMemoryUsage()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }

    public class InsufficientMemoryException : Exception
    {
        public long RequiredBytes { get; }
        public long AvailableBytes { get; }

        public InsufficientMemoryException(string message, long requiredBytes, long availableBytes = 0)
            : base(message)
        {
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
        }

        public InsufficientMemoryException(string message) : base(message)
        {
            RequiredBytes = 0;
            AvailableBytes = 0;
        }

        public InsufficientMemoryException(string message, Exception inner)
            : base(message, inner)
        {
            RequiredBytes = 0;
            AvailableBytes = 0;
        }
    }
}
