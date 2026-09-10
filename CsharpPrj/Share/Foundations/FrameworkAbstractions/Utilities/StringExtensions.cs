#nullable enable
using System.Runtime.CompilerServices;

namespace FrameworkAbstractions.Utilities
{
    public static class StringExtensions
    {
        public static string EnsurePathSeparator(this string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }

        public static string EnsureTrailingSeparator(this string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path += Path.DirectorySeparatorChar;
            return path;
        }

        public static string RemoveTrailingSeparator(this string path)
        {
            while (path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path = path.TrimEnd(Path.DirectorySeparatorChar);
            return path;
        }
    }
}
