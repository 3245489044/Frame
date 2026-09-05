namespace PathManager;

public static class ProjectPathManager
{
    private static string? _cachedRoot;
    private static readonly object _lock = new();

    private static readonly string[] _markerFiles = [];

    private static readonly string[] _markerDirs =
    [
        ".git",
    ];

    public static string FindProjectRoot(string? startPath = null)
    {
        if (_cachedRoot != null)
            return _cachedRoot;

        lock (_lock)
        {
            if (_cachedRoot != null)
                return _cachedRoot;

            var current = startPath ?? AppDomain.CurrentDomain.BaseDirectory;
            while (current != null)
            {
                foreach (var marker in _markerDirs)
                {
                    if (Directory.Exists(Path.Combine(current, marker)))
                    {
                        _cachedRoot = current;
                        return current;
                    }
                }

                foreach (var marker in _markerFiles)
                {
                    if (File.Exists(Path.Combine(current, marker)))
                    {
                        _cachedRoot = current;
                        return current;
                    }
                }

                var parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            _cachedRoot = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
            return _cachedRoot;
        }
    }

    public static void ResetCache()
    {
        _cachedRoot = null;
    }
}
