
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace MetaCommand
{
    public class FileSystemExecutor : BaseCommandExecutor
    {
        protected int DefaultTimeoutMs { get; set; } = 30000;

        public FileSystemExecutor()
            : base("filesystem", "FileSystem Executor", "1.0") { }


        public override async Task<OperationResult<object>> DoAsync(
            string action, string? target, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(target))
                return OperationResult<object>.FailureResult("Target is required", ErrorCode.InvalidParameter);

            try
            {
                var value = parameters?.GetValueOrDefault("value")?.ToString();

                return action?.ToLower() switch
                {
                    "write"  => await WriteFileAsync(target, value, ct),
                    "delete" => DeleteFileAsync(target),
                    "mkdir"  => CreateDirectoryAsync(target),
                    _ => OperationResult<object>.FailureResult($"不支持的操作: {action}", ErrorCode.ActionNotFound)
                };
            }
            catch (Exception ex)
            {
                return OperationResult<object>.FailureResult($"文件操作失败: {ex.Message}", ErrorCode.ExecutionFailed);
            }
        }

        private async Task<OperationResult<object>> WriteFileAsync(string path, string? content, CancellationToken ct)
        {
            var fullPath = GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullPath, content ?? string.Empty, ct);
            return OperationResult<object>.SuccessResult(content, $"已写入 {fullPath}");
        }

        private OperationResult<object> DeleteFileAsync(string path)
        {
            var fullPath = GetFullPath(path);
            if (!File.Exists(fullPath))
                return OperationResult<object>.FailureResult($"文件不存在: {fullPath}", ErrorCode.TargetNotFound);

            File.Delete(fullPath);
            return OperationResult<object>.SuccessResult(null, $"已删除 {fullPath}");
        }

        private OperationResult<object> CreateDirectoryAsync(string path)
        {
            var fullPath = GetFullPath(path);
            if (Directory.Exists(fullPath))
                return OperationResult<object>.SuccessResult(null, $"目录已存在: {fullPath}");

            Directory.CreateDirectory(fullPath);
            return OperationResult<object>.SuccessResult(null, $"已创建目录 {fullPath}");
        }


        public override async Task<OperationResult<object>> GetAsync(
            string entity, Dictionary<string, object>? queryParams, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entity))
                return OperationResult<object>.FailureResult("Entity is required", ErrorCode.InvalidParameter);

            try
            {
                var fullPath = GetFullPath(entity);
                if (!File.Exists(fullPath))
                    return OperationResult<object>.FailureResult($"文件不存在: {fullPath}", ErrorCode.TargetNotFound);

                var content = await File.ReadAllTextAsync(fullPath, ct);
                return OperationResult<object>.SuccessResult(content, $"已读取 {entity}");
            }
            catch (Exception ex)
            {
                return OperationResult<object>.FailureResult($"读取文件失败: {ex.Message}", ErrorCode.ExecutionFailed);
            }
        }


        public override Task<OperationResult<WorldModel>> LookAsync(
            string? scope, Dictionary<string, object>? options, CancellationToken ct = default)
        {
            try
            {
                var path = string.IsNullOrWhiteSpace(scope)
                    ? Directory.GetCurrentDirectory()
                    : GetFullPath(scope);

                if (!Directory.Exists(path))
                    return Task.FromResult(OperationResult<WorldModel>.FailureResult(
                        $"目录不存在: {path}", ErrorCode.TargetNotFound));

                var files = Directory.GetFiles(path);
                var directories = Directory.GetDirectories(path);

                var model = new WorldModel
                {
                    AdapterId = ExecutorId,
                    Scope = scope ?? ".",
                    Timestamp = DateTime.UtcNow
                };

                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    model.Entities.Add(new EntityDescriptor
                    {
                        Name = Path.GetFileName(file),
                        Type = "File",
                        Properties = new Dictionary<string, object>
                        {
                            ["Size"] = info.Length,
                            ["LastModified"] = info.LastWriteTime
                        }
                    });
                }

                foreach (var dir in directories)
                {
                    model.Entities.Add(new EntityDescriptor
                    {
                        Name = Path.GetFileName(dir),
                        Type = "Directory"
                    });
                }

                return Task.FromResult(OperationResult<WorldModel>.SuccessResult(model,
                    $"已列出 {path}（{files.Length} 个文件，{directories.Length} 个目录）"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<WorldModel>.FailureResult(
                    $"列出目录失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override async Task<OperationResult<bool>> WaitAsync(
            string condition, int timeoutMs, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return OperationResult<bool>.FailureResult("Condition is required", ErrorCode.InvalidParameter);

            try
            {
                var fullPath = GetFullPath(condition);
                var directory = Path.GetDirectoryName(fullPath) ?? ".";
                var fileName = Path.GetFileName(fullPath);

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs);
                using var registration = cts.Token.Register(() => tcs.TrySetResult(false));

                using var watcher = new FileSystemWatcher(directory, fileName);
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;

                FileSystemEventHandler handler = (s, e) =>
                {
                    if (e.Name == fileName)
                        tcs.TrySetResult(true);
                };
                RenamedEventHandler renameHandler = (s, e) =>
                {
                    if (e.Name == fileName || e.OldName == fileName)
                        tcs.TrySetResult(true);
                };

                watcher.Changed += handler;
                watcher.Created += handler;
                watcher.Deleted += handler;
                watcher.Renamed += renameHandler;
                watcher.EnableRaisingEvents = true;

                var result = await tcs.Task;
                return OperationResult<bool>.SuccessResult(result,
                    result ? "文件已变化" : "等待超时");
            }
            catch (Exception ex)
            {
                return OperationResult<bool>.FailureResult($"等待文件变化失败: {ex.Message}", ErrorCode.ExecutionFailed);
            }
        }


        public override Task<OperationResult<bool>> AssertAsync(
            string predicate, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(predicate))
                return Task.FromResult(OperationResult<bool>.FailureResult("Predicate is required", ErrorCode.InvalidParameter));

            try
            {
                var fullPath = GetFullPath(predicate);
                var mode = parameters?.GetValueOrDefault("expected")?.ToString()?.ToLower() ?? "exists";

                bool exists = mode switch
                {
                    "notexists" or "not_exist" or "not-exist" => !File.Exists(fullPath) && !Directory.Exists(fullPath),
                    _ => File.Exists(fullPath) || Directory.Exists(fullPath)
                };

                return Task.FromResult(OperationResult<bool>.SuccessResult(exists,
                    exists ? $"断言通过: {fullPath} 存在" : $"断言失败: {fullPath} 不存在"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<bool>.FailureResult($"断言失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override async Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(
            string? scope, CancellationToken ct = default)
        {
            var model = await LookAsync(scope, null, ct);
            if (!model.Success)
                return OperationResult<List<EntityDescriptor>>.FailureResult(model.Message, model.ErrorCode);

            return OperationResult<List<EntityDescriptor>>.SuccessResult(
                model.Data?.Entities ?? new List<EntityDescriptor>(),
                $"已发现 {(model.Data?.Entities?.Count ?? 0)} 个条目");
        }


        private static string GetFullPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }
    }
}
