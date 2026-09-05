
using System.Diagnostics;
using System.Text;

namespace TestInfrastructure
{

    public static class TestHelper
    {
        public static string GenerateTempFileName(string? prefix = null, string? extension = null)
        {
            return $"{prefix ?? "test"}_{Guid.NewGuid():N}{extension ?? ".tmp"}";
        }

        public static string GenerateTempDirectoryPath(string? prefix = null)
        {
            return Path.Combine(Path.GetTempPath(), $"{prefix ?? "test"}_{Guid.NewGuid():N}");
        }

        public static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000, int pollIntervalMs = 50)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(pollIntervalMs);
            }
            return false;
        }

        public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000, int pollIntervalMs = 50)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (await condition())
                    return true;
                await Task.Delay(pollIntervalMs);
            }
            return false;
        }

        public static bool Retry(Func<bool> action, int maxRetries = 3, int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (action())
                    return true;
                if (i < maxRetries - 1)
                    Thread.Sleep(delayMs);
            }
            return false;
        }

        public static async Task<bool> RetryAsync(Func<Task<bool>> action, int maxRetries = 3, int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (await action())
                    return true;
                if (i < maxRetries - 1)
                    await Task.Delay(delayMs);
            }
            return false;
        }

        public static TimeSpan MeasureTime(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed;
        }

        public static async Task<TimeSpan> MeasureTimeAsync(Func<Task> asyncAction)
        {
            var sw = Stopwatch.StartNew();
            await asyncAction();
            sw.Stop();
            return sw.Elapsed;
        }

        public static (T Result, TimeSpan Duration) MeasureTime<T>(Func<T> action)
        {
            var sw = Stopwatch.StartNew();
            var result = action();
            sw.Stop();
            return (result, sw.Elapsed);
        }

        public static async Task<(T Result, TimeSpan Duration)> MeasureTimeAsync<T>(Func<Task<T>> asyncAction)
        {
            var sw = Stopwatch.StartNew();
            var result = await asyncAction();
            sw.Stop();
            return (result, sw.Elapsed);
        }
    }


    public static class TestPathResolver
    {
        public static string? FindProjectRoot(string markerFile = "*.csproj")
        {
            var assemblyLocation = typeof(TestPathResolver).Assembly.Location;
            var directory = Path.GetDirectoryName(assemblyLocation);

            while (directory != null)
            {
                if (Directory.GetFiles(directory, markerFile).Length > 0)
                    return directory;
                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        public static string GetTestDataPath(string? relativePath = null)
        {
            var root = FindProjectRoot();
            var testDataDir = root != null
                ? Path.Combine(root, "TestData")
                : Path.Combine(Directory.GetCurrentDirectory(), "TestData");

            if (relativePath != null)
                testDataDir = Path.Combine(testDataDir, relativePath);

            return testDataDir;
        }

        public static string EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        public static string GetTestOutputDirectory(string testClassName, string? testName = null)
        {
            var baseDir = Path.Combine(
                Path.GetTempPath(),
                "TestInfrastructure",
                testClassName);
            if (testName != null)
                baseDir = Path.Combine(baseDir, testName);
            return EnsureDirectoryExists(baseDir);
        }
    }


    public class TestLogger
    {
        private readonly string _name;
        private readonly List<string> _entries = new();
        private readonly object _lock = new();

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public TestLogger(string name)
        {
            _name = name;
        }

        public IReadOnlyList<string> Entries
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        public bool HasErrors
        {
            get { lock (_lock) return _entries.Any(e => e.Contains("[ERROR]")); }
        }

        public bool HasWarnings
        {
            get { lock (_lock) return _entries.Any(e => e.Contains("[WARN]")); }
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);

        private void Log(LogLevel level, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var entry = $"[{timestamp}] [{level}] [{_name}] {message}";
            lock (_lock)
            {
                _entries.Add(entry);
            }
            Console.WriteLine(entry);
        }

        public void SaveToFile(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            lock (_lock)
            {
                File.WriteAllLines(filePath, _entries);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }


    public static class TestReportGenerator
    {
        public static string GenerateTextReport(TestRunSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"  测试报告: {summary.Name}");
            sb.AppendLine("========================================");
            sb.AppendLine($"  总测试数: {summary.Total}");
            sb.AppendLine($"  通过: {summary.Passed}");
            sb.AppendLine($"  失败: {summary.Failed}");
            sb.AppendLine($"  通过率: {summary.PassRate:F2}%");
            sb.AppendLine($"  总耗时: {summary.TotalDuration.TotalSeconds:F2}s");
            sb.AppendLine("========================================");

            if (summary.Failed > 0)
            {
                sb.AppendLine();
                sb.AppendLine("失败的测试:");
                foreach (var result in summary.Results.Where(r => !r.Passed))
                {
                    sb.AppendLine($"  [FAIL] {result.Name}");
                    if (result.ErrorMessage != null)
                        sb.AppendLine($"    原因: {result.ErrorMessage}");
                }
            }

            return sb.ToString();
        }

        public static string GenerateMarkdownReport(TestRunSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 测试报告: {summary.Name}");
            sb.AppendLine();
            sb.AppendLine("## 汇总");
            sb.AppendLine();
            sb.AppendLine("| 指标 | 值 |");
            sb.AppendLine("|------|-----|");
            sb.AppendLine($"| 总测试数 | {summary.Total} |");
            sb.AppendLine($"| 通过 | {summary.Passed} |");
            sb.AppendLine($"| 失败 | {summary.Failed} |");
            sb.AppendLine($"| 通过率 | {summary.PassRate:F2}% |");
            sb.AppendLine($"| 总耗时 | {summary.TotalDuration.TotalSeconds:F2}s |");
            sb.AppendLine();

            if (summary.Failed > 0)
            {
                sb.AppendLine("## 失败详情");
                sb.AppendLine();
                foreach (var result in summary.Results.Where(r => !r.Passed))
                {
                    sb.AppendLine($"- **{result.Name}**: {result.ErrorMessage ?? "未知错误"}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static TestRunSummary FromTestGroupResult(TestGroupResult groupResult)
        {
            var summary = new TestRunSummary
            {
                Name = groupResult.GroupName,
                TotalDuration = groupResult.Duration
            };

            foreach (var msg in groupResult.Messages)
            {
                var result = new TestResult();
                if (msg.StartsWith("[PASS]"))
                {
                    result.Name = msg.Replace("[PASS] ", "");
                    result.Passed = true;
                }
                else if (msg.StartsWith("[FAIL]"))
                {
                    result.Name = msg.Replace("[FAIL] ", "");
                    result.Passed = false;
                }
                else if (msg.StartsWith("[ERROR]"))
                {
                    var parts = msg.Replace("[ERROR] ", "").Split(": ", 2);
                    result.Name = parts[0];
                    result.Passed = false;
                    result.ErrorMessage = parts.Length > 1 ? parts[1] : "未知错误";
                }
                summary.Results.Add(result);
            }

            return summary;
        }
    }
}
