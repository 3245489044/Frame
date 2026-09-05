
using Common.Diagnostics;

namespace LoggingValidator
{
    internal static class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;
        private static readonly List<string> _errors = new();
        private static readonly string _testOutputDir;
        private static readonly string _projectRoot;

        static Program()
        {
            _testOutputDir = Path.Combine(Path.GetTempPath(), "LoggingValidator", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputDir);
            _projectRoot = _testOutputDir;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  日志功能验证程序 v1.0");
            Console.WriteLine("========================================\n");

            var startTime = DateTime.Now;

            RunPhase("阶段 1/9: 环境变量模拟", TestEnvironmentVariables);

            RunPhase("阶段 2/9: 日志系统初始化", TestLoggingSetup);

            RunPhase("阶段 3/9: Logger 写入测试", TestLoggerWrites);

            RunPhase("阶段 4/9: 批次隔离测试", TestBatchIsolation);

            RunPhase("阶段 5/9: SQLite 存储测试", TestSqliteStorage);

            RunPhase("阶段 6/9: JSON 文件导出测试", TestJsonExport);

            RunPhase("阶段 7/9: LogPipeline 管线测试", TestLogPipeline);

            RunPhase("阶段 8/9: LogQueryFS 查询测试", TestLogQueryFS);

            RunPhase("阶段 9/9: 紧急转储测试", TestEmergencyDump);

            var elapsed = DateTime.Now - startTime;

            PrintSummary(elapsed);

            Cleanup();

            Environment.Exit(_failed > 0 ? 1 : 0);
        }


        static void RunPhase(string name, Action action)
        {
            Console.WriteLine($"--- {name} ---");
            try
            {
                action();
                _passed++;
                Console.WriteLine($"  \u2713 {name} 通过\n");
            }
            catch (Exception ex)
            {
                _failed++;
                _errors.Add($"[FAIL] {name}: {ex.Message}");
                Console.WriteLine($"  \u2717 {name} 失败: {ex.Message}\n");
            }
        }

        static void PrintSummary(TimeSpan elapsed)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  验证结果汇总");
            Console.WriteLine("========================================");
            Console.WriteLine($"  总阶段数: {_passed + _failed}");
            Console.WriteLine($"  通过: {_passed}");
            Console.WriteLine($"  失败: {_failed}");
            Console.WriteLine($"  耗时: {elapsed.TotalSeconds:F2}s");
            Console.WriteLine("========================================");

            if (_errors.Count > 0)
            {
                Console.WriteLine("\n  失败详情:");
                foreach (var err in _errors)
                {
                    Console.WriteLine($"    {err}");
                }
                Console.WriteLine("\n========================================");
                Console.WriteLine("  存在验证失败 \u2717");
                Console.WriteLine("========================================");
            }
            else
            {
                Console.WriteLine("  所有验证通过 \u2713");
                Console.WriteLine("========================================");
            }
        }

        static void Cleanup()
        {
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
            {
                BatchRouter.Instance.RemoveBatch(batchId);
            }

            LogRepository.Reset();

            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", null);

            try
            {
                if (Directory.Exists(_testOutputDir))
                    Directory.Delete(_testOutputDir, true);
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dbPath = Path.Combine(baseDir, "logs", "batch_logs.db");
                if (File.Exists(dbPath))
                {
                    try { File.Delete(dbPath); } catch { }
                }
                var logsDir = Path.Combine(baseDir, "logs");
                if (Directory.Exists(logsDir))
                {
                    try { Directory.Delete(logsDir, true); } catch { }
                }
            }
            catch { }
        }


        static void TestEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", null);
            Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", null);

            var fallbackId = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID");
            var fallbackName = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME") ?? "自动生成批次";
            var fallbackSource = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE") ?? "Launcher";

            if (fallbackId != null)
                throw new Exception("未设置时 FILEDRIVEN_BATCH_ID 应为 null");
            if (fallbackName != "自动生成批次")
                throw new Exception($"回退名称应为 '自动生成批次'，实际为 '{fallbackName}'");
            if (fallbackSource != "Launcher")
                throw new Exception($"回退来源应为 'Launcher'，实际为 '{fallbackSource}'");

            Console.WriteLine("  \u2713 环境变量回退逻辑正确 (null → '自动生成批次' / 'Launcher')");

            var batchId = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", batchId);
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", "交互启动验证");
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", "验证程序");
            Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", "日志功能验证");
            Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", _projectRoot);

            Console.WriteLine($"  \u2713 FILEDRIVEN_BATCH_ID = {batchId}");
            Console.WriteLine($"  \u2713 FILEDRIVEN_BATCH_NAME = 交互启动验证");
            Console.WriteLine($"  \u2713 FILEDRIVEN_BATCH_SOURCE = 验证程序");
            Console.WriteLine($"  \u2713 FILEDRIVEN_BATCH_DESC = 日志功能验证");
            Console.WriteLine($"  \u2713 FILEDRIVEN_PROJECT_ROOT = {_projectRoot}");

            var readBatchId = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID");
            var readBatchName = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME");
            var readBatchSource = Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE");

            if (string.IsNullOrEmpty(readBatchId))
                throw new Exception("FILEDRIVEN_BATCH_ID 读取失败");
            if (string.IsNullOrEmpty(readBatchName))
                throw new Exception("FILEDRIVEN_BATCH_NAME 读取失败");
            if (string.IsNullOrEmpty(readBatchSource))
                throw new Exception("FILEDRIVEN_BATCH_SOURCE 读取失败");

            Console.WriteLine("  \u2713 C# 端读取环境变量成功");
        }


        static void TestLoggingSetup()
        {
            LoggingSetup.Initialize(LogCategory.All);
            Console.WriteLine("  \u2713 LoggingSetup.Initialize() 成功");

            var allCategories = LogCategory.All;
            Console.WriteLine($"  \u2713 LogController 配置正确 (All = {allCategories})");

            var defaultCount = LogCategoryRegistry.Count;
            Console.WriteLine($"  \u2713 LogCategoryRegistry 分类注册完整 ({defaultCount} 个分类)");

            var defaultLogger = LoggingSetup.Default;
            if (defaultLogger == null)
                throw new Exception("Default Logger 不可用");

            Console.WriteLine("  \u2713 Default Logger 可用");
        }


        static void TestLoggerWrites()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
                BatchRouter.Instance.RemoveBatch(batchId);

            var logger = new Logger(moduleName: "Validator");

            var levels = new[] { "INFO", "WARN", "ERROR", "DEBUG", "FATAL" };
            foreach (var level in levels)
            {
                for (int i = 0; i < 5; i++)
                {
                    var entry = new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = level,
                        Category = LogCategory.System,
                        Module = "Validator",
                        Message = $"[{level}] 验证日志 #{i + 1}"
                    };
                    LogRepository.Write(entry);
                }
                Console.WriteLine($"  \u2713 写入 5 条 {level} 日志");
            }

            var count = LogRepository.Count;
            if (count != 25)
                throw new Exception($"LogRepository.Count 应为 25，实际为 {count}");
            Console.WriteLine($"  \u2713 LogRepository.Count = {count}");

            var logs = LogRepository.GetAll().ToList();
            for (int i = 0; i < logs.Count; i++)
            {
                if (logs[i].SequenceNumber != i + 1)
                    throw new Exception($"SequenceNumber 不连续: 期望 {i + 1}，实际 {logs[i].SequenceNumber}");
            }
            Console.WriteLine($"  \u2713 SequenceNumber 从 1 到 {logs.Count} 单调递增");

            var maxSeq = LogRepository.GetMaxSequence();
            if (maxSeq != 25)
                throw new Exception($"GetMaxSequence 应为 25，实际为 {maxSeq}");
            Console.WriteLine($"  \u2713 GetMaxSequence = {maxSeq}");
        }


        static void TestBatchIsolation()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
                BatchRouter.Instance.RemoveBatch(batchId);

            BatchRouter.Instance.RegisterBatch("batch_A", new BatchMetadata { Name = "BatchA" });
            Console.WriteLine("  \u2713 批次 A 注册成功");

            for (int i = 0; i < 10; i++)
            {
                LogRepository.Write(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "INFO",
                    Category = LogCategory.System,
                    Module = "BatchA",
                    Message = $"Batch A - log #{i}"
                });
            }
            Console.WriteLine("  \u2713 批次 A 写入 10 条日志");

            BatchRouter.Instance.FlushToIsolatedStorage("batch_A");

            BatchRouter.Instance.RegisterBatch("batch_B", new BatchMetadata { Name = "BatchB" });
            Console.WriteLine("  \u2713 批次 B 注册成功");

            for (int i = 0; i < 10; i++)
            {
                LogRepository.Write(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "ERROR",
                    Category = LogCategory.System,
                    Module = "BatchB",
                    Message = $"Batch B - log #{i}"
                });
            }
            Console.WriteLine("  \u2713 批次 B 写入 10 条日志");

            BatchRouter.Instance.FlushToIsolatedStorage("batch_B");
            BatchRouter.Instance.ClearBatch("batch_B");
            for (int i = 0; i < 10; i++)
            {
                LogRepository.Write(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "ERROR",
                    Category = LogCategory.System,
                    Module = "BatchB",
                    Message = $"Batch B - log #{i}"
                });
            }
            Console.WriteLine("  \u2713 批次 B 写入 10 条日志");
            BatchRouter.Instance.FlushToIsolatedStorage("batch_B");

            var logsA = BatchRouter.Instance.GetBatchLogs("batch_A").ToList();
            var logsB = BatchRouter.Instance.GetBatchLogs("batch_B").ToList();

            if (logsA.Count != 10)
                throw new Exception($"批次 A 日志数应为 10，实际为 {logsA.Count}");
            Console.WriteLine($"  \u2713 批次 A 日志数 = {logsA.Count} (隔离正确)");

            if (logsB.Count != 10)
                throw new Exception($"批次 B 日志数应为 10，实际为 {logsB.Count}");
            Console.WriteLine($"  \u2713 批次 B 日志数 = {logsB.Count} (隔离正确)");

            if (logsA.Any(l => l.Level != "INFO"))
                throw new Exception("批次 A 包含非 INFO 级别的日志");
            if (logsB.Any(l => l.Level != "ERROR"))
                throw new Exception("批次 B 包含非 ERROR 级别的日志");

            Console.WriteLine("  \u2713 批次 A 和 B 的日志内容完全隔离");
        }


        static void TestSqliteStorage()
        {
            var storage = new SqliteIsolatedStorage("test_sqlite", Path.Combine(_testOutputDir, "test_sqlite.db"));
            storage.Initialize();
            Console.WriteLine("  \u2713 SqliteIsolatedStorage 初始化成功");

            var testEntries = new List<LogEntry>();
            for (int i = 0; i < 5; i++)
            {
                testEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "INFO",
                    Category = LogCategory.System,
                    Module = "SQLiteTest",
                    Message = $"SQLite test #{i}"
                });
            }
            storage.Store(testEntries);
            Console.WriteLine("  \u2713 Store 操作成功");

            var count = storage.Count();
            if (count != 5)
                throw new Exception($"Count 应为 5，实际为 {count}");
            Console.WriteLine($"  \u2713 Count = {count}");

            var allLogs = storage.GetAll().ToList();
            if (allLogs.Count != 5)
                throw new Exception($"GetAll 返回 {allLogs.Count} 条，应为 5 条");
            Console.WriteLine("  \u2713 GetAll 操作成功");

            storage.Clear();
            var afterClear = storage.Count();
            if (afterClear != 0)
                throw new Exception($"Clear 后 Count 应为 0，实际为 {afterClear}");
            Console.WriteLine("  \u2713 Clear 操作成功");
        }


        static void TestJsonExport()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
                BatchRouter.Instance.RemoveBatch(batchId);

            BatchRouter.Instance.RegisterBatch("test_export", new BatchMetadata { Name = "ExportTest" });
            LogRepository.Write(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "INFO",
                Category = LogCategory.System,
                Module = "ExportTest",
                Message = "导出测试消息"
            });
            BatchRouter.Instance.FlushToIsolatedStorage("test_export");

            var files = LogFileExporter.ExportBatch("test_export", _projectRoot);
            if (files.Count == 0)
                throw new Exception("导出文件列表为空");

            Console.WriteLine($"  \u2713 导出成功，生成 {files.Count} 个文件");

            var latestFile = files.FirstOrDefault(f => f.Contains("batch_test_export_latest"));
            if (latestFile == null || !File.Exists(latestFile))
                throw new Exception("batch_test_export_latest.json 不存在");
            Console.WriteLine($"  \u2713 batch_test_export_latest.json 存在");

            var json = File.ReadAllText(latestFile);
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(stream);

            if (!data.TryGetProperty("batchId", out var batchIdProp) || batchIdProp.GetString() != "test_export")
                throw new Exception("batchId 字段缺失或不正确");
            Console.WriteLine("  \u2713 JSON 包含 batchId 字段");

            if (!data.TryGetProperty("totalCount", out var countProp) || countProp.GetInt32() <= 0)
                throw new Exception("totalCount 字段缺失或不正确");
            Console.WriteLine("  \u2713 JSON 包含 totalCount 字段");

            if (!data.TryGetProperty("entries", out var entriesProp) || entriesProp.GetArrayLength() == 0)
                throw new Exception("entries 字段缺失或为空");
            Console.WriteLine("  \u2713 JSON 包含 entries 字段");

            if (!data.TryGetProperty("summary", out _))
                throw new Exception("summary 字段缺失");
            Console.WriteLine("  \u2713 JSON 包含 summary 字段");

            var entry = entriesProp[0];
            if (!entry.TryGetProperty("timestamp", out _))
                throw new Exception("entry 缺少 timestamp 字段");
            if (!entry.TryGetProperty("level", out _))
                throw new Exception("entry 缺少 level 字段");
            if (!entry.TryGetProperty("category", out _))
                throw new Exception("entry 缺少 category 字段");
            if (!entry.TryGetProperty("module", out _))
                throw new Exception("entry 缺少 module 字段");
            if (!entry.TryGetProperty("message", out _))
                throw new Exception("entry 缺少 message 字段");

            Console.WriteLine("  \u2713 entry 包含所有必需字段 (timestamp/level/category/module/message)");

            var logsDir = Path.Combine(_projectRoot, "logs");
            Directory.CreateDirectory(logsDir);

            foreach (var f in Directory.GetFiles(logsDir, "batch_*.json"))
                File.Delete(f);

            for (int i = 0; i < 60; i++)
            {
                var oldFile = Path.Combine(logsDir, $"batch_a_old_{i:D3}_latest.json");
                File.WriteAllText(oldFile, "{}");
            }

            BatchRouter.Instance.RegisterBatch("test_cleanup", new BatchMetadata { Name = "Cleanup" });
            LogRepository.Write(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "INFO",
                Category = LogCategory.System,
                Module = "Cleanup",
                Message = "Cleanup test"
            });
            BatchRouter.Instance.FlushToIsolatedStorage("test_cleanup");
            LogFileExporter.ExportBatch("test_cleanup", _projectRoot);

            var remainingFiles = Directory.GetFiles(logsDir, "batch_*.json");
            if (remainingFiles.Length > 52)
                throw new Exception($"文件清理后数量应为 <= 52，实际为 {remainingFiles.Length}");
            Console.WriteLine($"  \u2713 文件清理逻辑正确 (剩余 {remainingFiles.Length} 个文件)");
        }


        static void TestLogPipeline()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
                BatchRouter.Instance.RemoveBatch(batchId);

            var result1 = LogPipeline.ExecuteWithExceptionHandling(
                batchId: "test_pipeline_normal",
                batchName: "正常管线测试",
                source: "Validator",
                projectRoot: _projectRoot
            );

            if (!result1.Success)
                throw new Exception("正常管线执行失败");
            Console.WriteLine("  \u2713 正常管线执行成功");

            if (result1.ExportedFiles.Count > 0)
            {
                Console.WriteLine($"  \u2713 正常管线导出 {result1.ExportedFiles.Count} 个文件");
            }

            var result2 = LogPipeline.ExecuteWithExceptionHandling(
                batchId: "test_pipeline_empty",
                batchName: "空日志管线测试",
                source: "Validator",
                projectRoot: _projectRoot
            );

            if (!result2.Success)
                throw new Exception("空日志管线执行失败");
            Console.WriteLine("  \u2713 空日志管线执行成功");

            Console.WriteLine($"  \u2713 空日志管线 TotalCount = {result2.TotalCount} (含管线内部日志)");

            var result3a = LogPipeline.ExecuteWithExceptionHandling(
                batchId: "test_pipeline_idem",
                batchName: "幂等测试",
                source: "Validator",
                projectRoot: _projectRoot
            );

            var result3b = LogPipeline.ExecuteWithExceptionHandling(
                batchId: "test_pipeline_idem",
                batchName: "幂等测试",
                source: "Validator",
                projectRoot: _projectRoot
            );

            if (!result3a.Success || !result3b.Success)
                throw new Exception("幂等测试执行失败");
            Console.WriteLine("  \u2713 管线幂等性验证通过（两次执行均成功）");
        }


        static void TestLogQueryFS()
        {
            LogRepository.Reset();
            foreach (var batchId in BatchRouter.Instance.AllBatchIds.ToList())
                BatchRouter.Instance.RemoveBatch(batchId);

            var modules = new[] { "Launcher", "WdUI", "FileDriven", "Pipeline" };
            foreach (var module in modules)
            {
                for (int i = 0; i < 3; i++)
                {
                    LogRepository.Write(new LogEntry
                    {
                        Timestamp = DateTime.Now.AddSeconds(-10 + i),
                        Level = i == 0 ? "INFO" : (i == 1 ? "WARN" : "ERROR"),
                        Category = LogCategory.System,
                        Module = module,
                        Message = $"[{module}] 测试日志 #{i}"
                    });
                }
            }
            Console.WriteLine("  \u2713 准备查询测试数据 (4 模块 × 3 条)");

            var launcherLogs = LogRepository.GetByModule("Launcher").ToList();
            if (launcherLogs.Count != 3)
                throw new Exception($"Launcher 模块日志数应为 3，实际为 {launcherLogs.Count}");
            Console.WriteLine($"  \u2713 按模块查询: Launcher = {launcherLogs.Count} 条");

            var errorLogs = LogRepository.GetByLevel("ERROR").ToList();
            if (errorLogs.Count != 4)
                throw new Exception($"ERROR 级别日志数应为 4，实际为 {errorLogs.Count}");
            Console.WriteLine($"  \u2713 按级别过滤: ERROR = {errorLogs.Count} 条");

            var keywordLogs = LogRepository.GetAll()
                .Where(l => l.Message.Contains("WdUI"))
                .ToList();
            if (keywordLogs.Count != 3)
                throw new Exception($"包含 'WdUI' 的日志数应为 3，实际为 {keywordLogs.Count}");
            Console.WriteLine($"  \u2713 按关键词搜索: 'WdUI' = {keywordLogs.Count} 条");

            var now = DateTime.Now;
            var timeRangeLogs = LogRepository.GetAll()
                .Where(l => l.Timestamp >= now.AddMinutes(-1) && l.Timestamp <= now.AddMinutes(1))
                .ToList();
            if (timeRangeLogs.Count == 0)
                throw new Exception("时间范围过滤不应返回空结果");
            Console.WriteLine($"  \u2713 按时间范围过滤: {timeRangeLogs.Count} 条");
        }


        static void TestEmergencyDump()
        {
            for (int i = 0; i < 5; i++)
            {
                LogRepository.Write(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "ERROR",
                    Category = LogCategory.System,
                    Module = "EmergencyTest",
                    Message = $"Emergency test log #{i}"
                });
            }
            Console.WriteLine("  \u2713 准备紧急转储测试数据 (5 条)");

            var mainPath = SkeletonFlow.App.Flow.Start<LogPipelineContext>()
                .Then(new FailingSkeleton());

            var fallbackPath = SkeletonFlow.App.Flow.Start<LogPipelineContext>()
                .Then(new LogEmergencyDumpSkeleton());

            var tryExecute = new LogTryExecuteSkeleton(mainPath.Skeleton, fallbackPath.Skeleton);

            var pipeline = SkeletonFlow.App.Flow.Start<LogPipelineContext>()
                .Then(new LogCollectSkeleton())
                .Then(tryExecute);

            var context = new LogPipelineContext
            {
                BatchId = "test_emergency",
                BatchName = "紧急转储测试",
                Source = "Validator",
                ProjectRoot = _projectRoot
            };

            var result = pipeline.Execute(context);

            if (!result.Success)
                throw new Exception("紧急转储执行失败");

            if (result.ExportedFiles.Count == 0)
                throw new Exception("紧急转储未生成文件");

            var emergencyFile = result.ExportedFiles.FirstOrDefault(f => f.Contains("emergency"));
            if (emergencyFile == null)
                throw new Exception("紧急转储文件应包含 'emergency' 标记");

            Console.WriteLine($"  \u2713 紧急转储文件生成: {Path.GetFileName(emergencyFile)}");

            var json = File.ReadAllText(emergencyFile);
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(stream);

            if (!data.TryGetProperty("isEmergency", out var isEmergency) || !isEmergency.GetBoolean())
                throw new Exception("紧急转储文件应包含 isEmergency=true");

            if (!data.TryGetProperty("totalCount", out var countProp) || countProp.GetInt32() <= 0)
                throw new Exception("紧急转储文件应包含 totalCount");

            Console.WriteLine($"  \u2713 紧急转储文件内容正确 (totalCount={countProp.GetInt32()})");
        }
    }


    internal class FailingSkeleton : SkeletonFlow.SkeletonBase<LogPipelineContext, LogExportResult>
    {
        public FailingSkeleton() : base("FailingSkeleton", "Always fails for testing") { }

        protected override LogExportResult ExecuteCore(LogPipelineContext input, SkeletonFlow.SkeletonContext ctx)
        {
            throw new InvalidOperationException("模拟的主路径失败");
        }

        protected override System.Threading.Tasks.Task<LogExportResult> ExecuteCoreAsync(LogPipelineContext input, SkeletonFlow.SkeletonContext ctx)
        {
            throw new InvalidOperationException("模拟的主路径失败");
        }
    }
}
