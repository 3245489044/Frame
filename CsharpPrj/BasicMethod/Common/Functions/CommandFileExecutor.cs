
namespace Modules.Common.Functions
{
    public class CommandFileLineResult
    {
        public string Command { get; set; } = string.Empty;

        public bool Success { get; set; }

        public string? Message { get; set; }

        public string? Error { get; set; }
    }

    public class CommandFileExecutor
    {
        private readonly Func<string, Task<bool>> _executeLineAsync;

        public CommandFileExecutor(Func<string, Task<bool>> executeLineAsync)
        {
            _executeLineAsync = executeLineAsync ?? throw new ArgumentNullException(nameof(executeLineAsync));
        }

        public async Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(
            string filePath, Action<string>? outputAction = null, int lineDelayMs = 0)
        {
            int successCount = 0, failCount = 0;
            await ProcessLinesAsync(filePath, async trimmed =>
            {
                var success = await _executeLineAsync(trimmed);
                if (success) { successCount++; outputAction?.Invoke($"✓ {trimmed}"); }
                else { failCount++; outputAction?.Invoke($"✗ {trimmed}  ← 执行失败"); }
            }, lineDelayMs);
            return (successCount, failCount);
        }

        public static async Task<List<CommandFileLineResult>> ExecuteFileWithResultsAsync(
            string filePath, Func<string, Task<CommandFileLineResult>> executeWithResultAsync, int lineDelayMs = 0)
        {
            var results = new List<CommandFileLineResult>();
            await ProcessLinesAsync(filePath, async trimmed =>
            {
                var result = await executeWithResultAsync(trimmed);
                results.Add(result);
            }, lineDelayMs);
            return results;
        }

        private static async Task ProcessLinesAsync(string filePath, Func<string, Task> processLineAsync, int lineDelayMs = 0)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Command file not found", filePath);

            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                    continue;
                await processLineAsync(trimmed);
                if (lineDelayMs > 0)
                    await Task.Delay(lineDelayMs);
            }
        }
    }
}
