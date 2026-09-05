#nullable disable
namespace Common.FileProcess
{
    using System.IO;

    public static class FileMethod
    {
        public static string FileRead(string path, Func<string, string> pathfunc = null)
        {
            if (pathfunc == null) pathfunc = StreamingPath;
            path = pathfunc(path);

            return System.IO.File.ReadAllText(path);
        }

        public static void FileSave(string path, string content, Func<string, string> pathfunc = null)
        {
            path = MainPathCreat(path, pathfunc);

            System.IO.File.WriteAllText(path, content);

        }

        public static string MainPathCreat(string path, Func<string, string> pathfunc = null)
        {
            if (pathfunc == null) pathfunc = StreamingPath;
            path = pathfunc(path);

            string directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            return path;
        }

        private static string StreamingPath(string path)
        {
            return path;
        }
    }
}
