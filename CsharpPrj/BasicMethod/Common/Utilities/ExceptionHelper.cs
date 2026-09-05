
#nullable enable

namespace Modules.Common.Utilities
{
    internal static class ExceptionHelper
    {
        public static void ThrowIfUniqueIdGenerationFailed(string baseId, int maxAttempts, string entityType)
        {
            throw new InvalidOperationException(
                $"Failed to generate unique {entityType} for '{baseId}' after {maxAttempts} attempts.");
        }

        public static void ThrowIfNull(object? value, string paramName)
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
        }

        public static void ThrowIfNullOrEmpty(string? value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Value cannot be null or empty", paramName);
        }

        public static void ThrowIfEmpty<T>(IReadOnlyCollection<T>? collection, string paramName)
        {
            if (collection == null || collection.Count == 0)
                throw new ArgumentException("Collection cannot be null or empty", paramName);
        }

        public static void ThrowIfOutOfRange(int index, int count, string paramName)
        {
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(paramName, $"Index {index} is out of range [0, {count}).");
        }
    }
}
