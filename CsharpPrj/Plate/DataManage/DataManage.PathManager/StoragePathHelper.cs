using System;
using System.IO;

namespace DataManage.PathManager
{
    public static class StoragePathHelper
    {
        public static string EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            var directory = Path.GetDirectoryName(path) ?? path;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return path;
        }

        public static string GetApplicationDataPath(string appName = "DataManage")
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, appName);
        }

        public static string GetLocalApplicationDataPath(string appName = "DataManage")
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, appName);
        }

        public static string CombinePaths(params string[] paths)
        {
            return Path.Combine(paths);
        }

        public static string GetSafeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException(nameof(fileName));

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            return fileName;
        }

        public static string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        public static bool IsPathRooted(string path)
        {
            return Path.IsPathRooted(path);
        }

        public static string GetDirectoryName(string path)
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }

        public static string GetFileName(string path)
        {
            return Path.GetFileName(path);
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        public static string GetExtension(string path)
        {
            return Path.GetExtension(path);
        }

        public static string ChangeExtension(string path, string extension)
        {
            return Path.ChangeExtension(path, extension);
        }
    }
}
