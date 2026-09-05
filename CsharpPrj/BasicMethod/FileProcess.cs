#region USING
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common;
#endregion
#nullable disable

namespace GraphMindStudio.FileProcess
{
    public static class FileProcess
    {
        public static async Task<bool> SaveToFileAsync(byte[] data, string path, Func<string, string> pathfunc = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "Data cannot be null");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            pathfunc ??= LocalPath.StreamingPath;
            string resolvedPath = pathfunc(path);

            string directory = Path.GetDirectoryName(resolvedPath);

            if (!await EnsureDirectoryAsync(directory))
                throw new IOException($"Failed to create directory: {directory}");

            try
            {
                using var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                await fileStream.WriteAsync(data, 0, data.Length);
                await fileStream.FlushAsync();

                if (!File.Exists(resolvedPath))
                    throw new IOException("File creation failed - file does not exist");

                var fileInfo = new FileInfo(resolvedPath);
                if (fileInfo.Length != data.Length)
                    throw new IOException($"File size mismatch. Expected: {data.Length}, Actual: {fileInfo.Length}");

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new IOException($"Error saving file at path '{resolvedPath}': {ex.Message}", ex);
            }
        }

        private static async Task<bool> EnsureDirectoryAsync(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return false;

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    await Task.Run(() => Directory.CreateDirectory(directoryPath));
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugPrintProvider.DebugPrint($"Failed to create directory: {ex.Message}");
                return false;
            }
        }

        public static string MainPath(string path, Func<string, string> pathfunc = null)
        {
            pathfunc ??= LocalPath.StreamingPath;
            return pathfunc(path);
        }

        public static bool RenameFolder(string originalPath, string newFolderName)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
                throw new ArgumentException("Original path cannot be null or empty", nameof(originalPath));

            if (string.IsNullOrWhiteSpace(newFolderName))
                throw new ArgumentException("New folder name cannot be null or empty", nameof(newFolderName));

            if (!Directory.Exists(originalPath))
                throw new DirectoryNotFoundException($"Specified directory does not exist: {originalPath}");

            string parentDirectory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(parentDirectory))
                throw new InvalidOperationException("Cannot determine parent directory of original path");

            string newPath = Path.Combine(parentDirectory, newFolderName);

            if (Directory.Exists(newPath))
                throw new InvalidOperationException($"Target directory already exists: {newPath}");

            try
            {
                Directory.Move(originalPath, newPath);
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to rename folder: {ex.Message}", ex);
            }
        }

        public static string FileRead(string path, Func<string, string> pathfunc = null)
        {
            pathfunc ??= LocalPath.StreamingPath;
            string resolvedPath = pathfunc(path);
            return File.ReadAllText(resolvedPath, Encoding.UTF8);
        }

        public static async Task<string> FileReadAsync(string path, Func<string, string> pathfunc = null)
        {
            pathfunc ??= LocalPath.StreamingPath;
            string resolvedPath = pathfunc(path);

            using var reader = new StreamReader(resolvedPath, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public static void FileSave(string path, string content, Func<string, string> pathfunc = null)
        {
            string resolvedPath = MainPathCreat(path, pathfunc);
            File.WriteAllText(resolvedPath, content, Encoding.UTF8);
        }

        public static async Task FileSaveAsync(string path, string content, Func<string, string> pathfunc = null)
        {
            string resolvedPath = MainPathCreat(path, pathfunc);

            using var writer = new StreamWriter(resolvedPath, false, Encoding.UTF8);
            await writer.WriteAsync(content);
            await writer.FlushAsync();
        }

        public static string MainPathCreat(string path, Func<string, string> pathfunc = null)
        {
            pathfunc ??= LocalPath.StreamingPath;
            string resolvedPath = pathfunc(path);

            string directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            return resolvedPath;
        }
    }
}