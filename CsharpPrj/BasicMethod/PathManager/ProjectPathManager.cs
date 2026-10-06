namespace PathManager;

public static class ProjectPathManager
{
    private static string? _cachedRoot;
    private static readonly object _lock = new();

    // Main 逻辑根特征：同层须同时含这两条子路径（与 Route.RepositoryRootResolver 同判据）。
    private static readonly string[] _mainFeatureDirs =
    [
        "Projects",
        Path.Combine("Frame", "CsharpPrj"),
    ];

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

            var start = startPath ?? AppDomain.CurrentDomain.BaseDirectory;

            // 1) 目录特征判定 Main：消除"就近 .git"在子模块拓扑下的歧义
            //    （Frame/Projects 子模块自带 .git 目录时，就近判定会误命到子模块而非仓库根）。
            //    Main 根下分别派生 <Root>\Projects 与 <Root>\Frame\CsharpPrj。
            var main = FindUpward(start, HasMainFeatures);
            if (main != null)
            {
                _cachedRoot = main;
                return _cachedRoot;
            }

            // 2) 回退"就近 .git"（非 Main 拓扑 / 仅检出单个子模块时的历史行为）。
            var current = start;
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

    private static string? FindUpward(string start, Func<string, bool> predicate)
    {
        var current = start;
        while (!string.IsNullOrEmpty(current))
        {
            if (predicate(current))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null || parent.FullName == current)
                break;

            current = parent.FullName;
        }

        return null;
    }

    private static bool HasMainFeatures(string dir)
    {
        foreach (var feature in _mainFeatureDirs)
        {
            if (!Directory.Exists(Path.Combine(dir, feature)))
                return false;
        }

        return true;
    }

    public static void ResetCache()
    {
        _cachedRoot = null;
    }
}
