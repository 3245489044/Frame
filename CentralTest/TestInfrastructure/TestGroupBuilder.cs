
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestInfrastructure
{
    public class TestGroupBuilder
    {
        private readonly string _groupName;
        private readonly List<TestCase> _testCases = new();

        private class TestCase
        {
            public string Name { get; set; } = string.Empty;
            public Func<bool>? SyncTest { get; set; }
            public Func<Task<bool>>? AsyncTest { get; set; }
            public Action? SyncAction { get; set; }
            public Func<Task>? AsyncAction { get; set; }
            public bool IsAsync => AsyncTest != null || AsyncAction != null;
        }

        public TestGroupBuilder(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("测试组名称不能为空", nameof(groupName));
            _groupName = groupName;
        }

        public TestGroupBuilder Add(string name, Func<bool> test)
        {
            ValidateTestCaseName(name);
            _testCases.Add(new TestCase { Name = name, SyncTest = test ?? throw new ArgumentNullException(nameof(test)) });
            return this;
        }

        public TestGroupBuilder Add(string name, Func<Task<bool>> testAsync)
        {
            ValidateTestCaseName(name);
            _testCases.Add(new TestCase { Name = name, AsyncTest = testAsync ?? throw new ArgumentNullException(nameof(testAsync)) });
            return this;
        }

        public TestGroupBuilder Add(string name, Action testAction)
        {
            ValidateTestCaseName(name);
            _testCases.Add(new TestCase { Name = name, SyncAction = testAction ?? throw new ArgumentNullException(nameof(testAction)) });
            return this;
        }

        public TestGroupBuilder Add(string name, Func<Task> testActionAsync)
        {
            ValidateTestCaseName(name);
            _testCases.Add(new TestCase { Name = name, AsyncAction = testActionAsync ?? throw new ArgumentNullException(nameof(testActionAsync)) });
            return this;
        }

        public TestGroupResult Run()
        {
            return Task.Run(async () => await RunAsync()).GetAwaiter().GetResult();
        }

        public async Task<TestGroupResult> RunAsync()
        {
            var result = new TestGroupResult { GroupName = _groupName };
            var sw = Stopwatch.StartNew();

            foreach (var tc in _testCases)
            {
                await ExecuteTestCaseAsync(result, tc);
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        private static async Task ExecuteTestCaseAsync(TestGroupResult result, TestCase tc)
        {
            try
            {
                if (tc.AsyncTest != null)
                {
                    var success = await tc.AsyncTest();
                    RecordResult(result, tc.Name, success);
                }
                else if (tc.AsyncAction != null)
                {
                    await tc.AsyncAction();
                    result.Passed++;
                    result.Messages.Add($"[PASS] {tc.Name}");
                }
                else if (tc.SyncTest != null)
                {
                    var success = tc.SyncTest();
                    RecordResult(result, tc.Name, success);
                }
                else if (tc.SyncAction != null)
                {
                    tc.SyncAction();
                    result.Passed++;
                    result.Messages.Add($"[PASS] {tc.Name}");
                }
            }
            catch (AssertFailedException)
            {
                result.Failed++;
                result.Messages.Add($"[FAIL] {tc.Name}");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Messages.Add($"[ERROR] {tc.Name}: {ex.Message}");
            }
        }

        private static void RecordResult(TestGroupResult result, string name, bool success)
        {
            if (success)
            {
                result.Passed++;
                result.Messages.Add($"[PASS] {name}");
            }
            else
            {
                result.Failed++;
                result.Messages.Add($"[FAIL] {name}");
            }
        }

        private void ValidateTestCaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("测试用例名称不能为空", nameof(name));
            if (_testCases.Any(tc => tc.Name == name))
                throw new ArgumentException($"测试用例名称 '{name}' 已存在，不允许重复", nameof(name));
        }
    }

    public class TestGroupResult
    {
        public string GroupName { get; set; } = string.Empty;
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<string> Messages { get; set; } = new();
        public TimeSpan Duration { get; set; }

        public int Total => Passed + Failed + Skipped;
        public double PassRate => Total > 0 ? (double)Passed / Total * 100 : 0;
        public bool AllPassed => Failed == 0;
    }
}
