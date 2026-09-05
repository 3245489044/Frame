
namespace TestInfrastructure
{

    public static class CleanupHelper
    {
        public static void ExecuteCleanup(params Action[] cleanupActions)
        {
            var exceptions = new List<Exception>();
            foreach (var action in cleanupActions)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(
                    $"清理过程中发生 {exceptions.Count} 个异常", exceptions);
            }
        }

        public static async Task ExecuteCleanupAsync(params Func<Task>[] cleanupActions)
        {
            var exceptions = new List<Exception>();
            foreach (var action in cleanupActions)
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(
                    $"清理过程中发生 {exceptions.Count} 个异常", exceptions);
            }
        }

        public static bool SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool SafeDeleteDirectory(string path, bool recursive = true)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool SafeClearDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return true;

                foreach (var file in Directory.GetFiles(path))
                {
                    SafeDeleteFile(file);
                }
                foreach (var dir in Directory.GetDirectories(path))
                {
                    SafeDeleteDirectory(dir);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }


    public class TempDirectoryScope : IDisposable
    {
        private readonly bool _autoCleanup;

        public string DirectoryPath { get; }

        public string Name => Path.GetFileName(DirectoryPath);

        public TempDirectoryScope(string? prefix = null, bool autoCleanup = true)
        {
            _autoCleanup = autoCleanup;
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"{prefix ?? "test"}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string CreateFile(string fileName, string? content = null)
        {
            var filePath = Path.Combine(DirectoryPath, fileName);
            if (content != null)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, content);
            }
            else
            {
                File.Create(filePath).Dispose();
            }
            return filePath;
        }

        public string CreateSubDirectory(string name)
        {
            var dirPath = Path.Combine(DirectoryPath, name);
            Directory.CreateDirectory(dirPath);
            return dirPath;
        }

        public string GetFilePath(string fileName)
        {
            return Path.Combine(DirectoryPath, fileName);
        }

        public void Dispose()
        {
            if (_autoCleanup)
            {
                CleanupHelper.SafeDeleteDirectory(DirectoryPath);
            }
        }
    }


    public class TestDataIsolator : IDisposable
    {
        private readonly string _baseDirectory;
        private readonly List<string> _createdDirectories = new();
        private readonly object _lock = new();

        public string CurrentIsolationDirectory { get; private set; }

        public TestDataIsolator(string? baseDirectory = null)
        {
            _baseDirectory = baseDirectory ?? Path.Combine(Path.GetTempPath(), "TestIsolation");
            CurrentIsolationDirectory = CreateIsolationDirectoryInternal();
        }

        public string CreateIsolationDirectory()
        {
            return CreateIsolationDirectoryInternal();
        }

        private string CreateIsolationDirectoryInternal()
        {
            var dir = Path.Combine(_baseDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            lock (_lock)
            {
                _createdDirectories.Add(dir);
            }
            CurrentIsolationDirectory = dir;
            return dir;
        }

        public string CreateFile(string fileName, string content)
        {
            var filePath = Path.Combine(CurrentIsolationDirectory, fileName);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        public string ReadFile(string fileName)
        {
            return File.ReadAllText(Path.Combine(CurrentIsolationDirectory, fileName));
        }

        public void Cleanup()
        {
            List<string> dirs;
            lock (_lock)
            {
                dirs = new List<string>(_createdDirectories);
                _createdDirectories.Clear();
            }

            foreach (var dir in dirs)
            {
                CleanupHelper.SafeDeleteDirectory(dir);
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
