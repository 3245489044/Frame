
namespace CommandFramework.Utilities;

public static class ProjectRootLocator
{
    public static string? FindRootUpward(string startPath)
    {
        if (string.IsNullOrEmpty(startPath)) return null;

        var primaryMatch = FindUpwardByMarker(startPath, dir => Directory.Exists(Path.Combine(dir, ".git")));
        if (primaryMatch != null)
            return primaryMatch;

        return FindUpwardByMarker(startPath, dir =>
            File.Exists(Path.Combine(dir, ".gitignore")) ||
            Directory.GetFiles(dir, "*.sln").Length > 0);
    }

    private static string? FindUpwardByMarker(string startPath, Func<string, bool> isMatch)
    {
        var current = startPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (isMatch(current))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (parent == current || parent == null) break;
            current = parent;
        }

        return null;
    }
}