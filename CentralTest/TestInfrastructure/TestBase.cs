
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestInfrastructure
{

    public abstract class TestBase
    {
        private Stopwatch? _testTimer;

        protected string? CurrentTestName { get; private set; }

        protected DateTime CurrentTestStartTime { get; private set; }

        protected TimeSpan CurrentTestDuration =>
            _testTimer?.Elapsed ?? TimeSpan.Zero;

        public TestContext? TestContext { get; set; }


        [TestInitialize]
        public virtual void TestInitialize()
        {
            CurrentTestName = TestContext?.TestName ?? "Unknown";
            CurrentTestStartTime = DateTime.UtcNow;
            _testTimer = Stopwatch.StartNew();
        }

        [TestCleanup]
        public virtual void TestCleanup()
        {
            _testTimer?.Stop();
        }


        protected string GetFullTestName()
        {
            return $"{GetType().Name}.{CurrentTestName}";
        }

        protected void LogInfo(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var testName = CurrentTestName ?? "Unknown";
            Console.WriteLine($"[{timestamp}] [INFO] [{testName}] {message}");
        }

        protected void LogWarning(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var testName = CurrentTestName ?? "Unknown";
            Console.WriteLine($"[{timestamp}] [WARN] [{testName}] {message}");
        }

        protected void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var testName = CurrentTestName ?? "Unknown";
            Console.WriteLine($"[{timestamp}] [ERROR] [{testName}] {message}");
        }

        protected void AssertMaxDuration(TimeSpan maxDuration)
        {
            var elapsed = CurrentTestDuration;
            Assert.IsTrue(elapsed <= maxDuration,
                $"测试耗时 {elapsed.TotalMilliseconds:F0}ms 超过最大允许值 {maxDuration.TotalMilliseconds:F0}ms");
        }
    }


    public abstract class SkeletonTestBase : TestBase
    {
        protected static TOutput ExecuteSkeleton<TInput, TOutput>(
            ISkeleton<TInput, TOutput> skeleton,
            TInput input)
        {
            return skeleton.Execute(input, new SkeletonContext());
        }

        protected static async Task<TOutput> ExecuteSkeletonAsync<TInput, TOutput>(
            ISkeleton<TInput, TOutput> skeleton,
            TInput input)
        {
            return await skeleton.ExecuteAsync(input, new SkeletonContext());
        }

        protected static TContext ExecuteSkeletonChain<TContext>(
            TContext initialContext,
            params ISkeleton<TContext, TContext>[] skeletons)
            where TContext : class
        {
            var context = initialContext;
            foreach (var skeleton in skeletons)
            {
                context = skeleton.Execute(context, new SkeletonContext());
            }
            return context;
        }

        protected static async Task<TContext> ExecuteSkeletonChainAsync<TContext>(
            TContext initialContext,
            params ISkeleton<TContext, TContext>[] skeletons)
            where TContext : class
        {
            var context = initialContext;
            foreach (var skeleton in skeletons)
            {
                context = await skeleton.ExecuteAsync(context, new SkeletonContext());
            }
            return context;
        }
    }


    public abstract class IntegrationTestBase : TestBase
    {
        private readonly List<string> _tempFiles = new();

        private readonly List<string> _tempDirectories = new();

        protected string CreateTempFile(string? content = null, string? extension = null)
        {
            var fileName = $"{Guid.NewGuid():N}{extension ?? ".tmp"}";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            if (content != null)
            {
                File.WriteAllText(tempPath, content);
            }
            _tempFiles.Add(tempPath);
            return tempPath;
        }

        protected string CreateTempDirectory()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);
            _tempDirectories.Add(tempPath);
            return tempPath;
        }

        protected void WriteFile(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
        }

        protected string ReadFile(string path)
        {
            return File.ReadAllText(path);
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            foreach (var file in _tempFiles)
            {
                CleanupHelper.SafeDeleteFile(file);
            }
            foreach (var dir in _tempDirectories)
            {
                CleanupHelper.SafeDeleteDirectory(dir);
            }
            base.TestCleanup();
        }
    }


    public abstract class FileOutputTestBase : TestBase
    {
        protected string? OutputDirectory { get; private set; }

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            // 路径策略：保留原「%TEMP%\TestOutput\<类>\<测试>」语义（历史输出兼容）。
            // 统一路径工具见 TestPathResolver.GetTestOutputDirectory(...)（新测试可切换，旧输出路径不回迁）。
            OutputDirectory = Path.Combine(
                Path.GetTempPath(),
                "TestOutput",
                GetType().Name,
                CurrentTestName ?? "Unknown");
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }
        }

        protected string GetOutputPath(string fileName)
        {
            return Path.Combine(OutputDirectory!, fileName);
        }

        protected void WriteOutput(string fileName, string content)
        {
            var path = GetOutputPath(fileName);
            File.WriteAllText(path, content);
            LogInfo($"输出文件已写入: {path}");
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            try
            {
                if (OutputDirectory != null && Directory.Exists(OutputDirectory))
                {
                    Directory.Delete(OutputDirectory, true);
                }
            }
            catch { }
            base.TestCleanup();
        }
    }


    public abstract class IsolatedTestBase : TestBase
    {
        private IFixtureScope? _currentScope;

        protected virtual IFixtureScope CreateScope()
        {
            return new NullFixtureScope();
        }

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _currentScope = CreateScope();
            _currentScope.Enter();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _currentScope?.Exit();
            _currentScope = null;
            base.TestCleanup();
        }

        private class NullFixtureScope : IFixtureScope
        {
            public void Enter() { }
            public void Exit() { }
            public void Dispose() { }
        }
    }


    public interface ISkeleton<in TInput, TOutput>
    {
        TOutput Execute(TInput input, SkeletonContext context);

        Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context);
    }

    public class SkeletonContext
    {
        public CancellationToken CancellationToken { get; set; }

        public Dictionary<string, object> Items { get; set; } = new();

        public T? GetItem<T>(string key)
        {
            return Items.TryGetValue(key, out var value) && value is T t ? t : default;
        }

        public void SetItem(string key, object value)
        {
            Items[key] = value;
        }
    }

    public class DelegateSkeleton<TInput, TOutput> : ISkeleton<TInput, TOutput>
    {
        private readonly Func<TInput, SkeletonContext, TOutput>? _syncFunc;
        private readonly Func<TInput, SkeletonContext, Task<TOutput>>? _asyncFunc;
        private readonly string _name;

        public DelegateSkeleton(
            string name,
            Func<TInput, SkeletonContext, TOutput>? syncWithCtx = null,
            Func<TInput, SkeletonContext, Task<TOutput>>? asyncWithCtx = null,
            string? description = null)
        {
            _name = name;
            _syncFunc = syncWithCtx;
            _asyncFunc = asyncWithCtx;
            Description = description;
        }

        public string Name => _name;

        public string? Description { get; }

        public TOutput Execute(TInput input, SkeletonContext context)
        {
            if (_syncFunc != null)
                return _syncFunc(input, context);
            if (_asyncFunc != null)
                return _asyncFunc(input, context).GetAwaiter().GetResult();
            throw new InvalidOperationException($"Skeleton '{_name}' 没有注册任何执行委托");
        }

        public async Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context)
        {
            if (_asyncFunc != null)
                return await _asyncFunc(input, context);
            if (_syncFunc != null)
                return _syncFunc(input, context);
            throw new InvalidOperationException($"Skeleton '{_name}' 没有注册任何执行委托");
        }
    }
}
