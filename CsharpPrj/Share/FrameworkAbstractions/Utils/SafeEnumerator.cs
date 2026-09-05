#nullable enable

namespace FrameworkAbstractions.Utilities;

public static class SafeEnumerator
{
    public static bool ForEachSafe<T>(
        System.Collections.IEnumerable collection,
        Func<T, bool> predicate,
        Func<T, bool>? isValid = null)
    {
        try
        {
            foreach (T item in collection)
            {
                if (isValid != null && !isValid(item))
                    continue;
                try
                {
                    if (predicate(item))
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    continue;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
        }
        return false;
    }
}