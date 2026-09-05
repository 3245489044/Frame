
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LogDiagnosticTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
            LogController.Configure(LogCategory.All);
        }

        [TestMethod]
        public void Diag_QueryFS_TimeFilter_Since100y()
        {
            LogRepository.Reset();
            var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };

            logger.Info(LogCategory.System, "Test message");

            var filter = new LogQueryFilter
            {
                Since = "100y",
                Limit = 100
            };

            var queryResult = LogQueryFS.Query(filter);
            Console.WriteLine($"TotalCount={queryResult.TotalCount}, Entries.Count={queryResult.Entries.Count}");
            foreach (var e in queryResult.Entries)
            {
                Console.WriteLine($"  [{e.Timestamp:HH:mm:ss.fff}] {e.Message}");
            }

            var fromTime = filter.GetFromTime();
            Console.WriteLine($"GetFromTime() returned: {(fromTime.HasValue ? fromTime.Value.ToString("o") : "null")}");

            Assert.AreEqual(1, queryResult.Entries.Count, "Since=100y 应该匹配到刚写入的日志");
        }

        [TestMethod]
        public void Diag_QueryFS_EmptyResult_NonExistentCategory()
        {
            LogRepository.Reset();
            var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };

            logger.Info(LogCategories.WdUI_Layout, "Layout message");

            var filter = new LogQueryFilter
            {
                Path = "NonExistent/Category",
                Limit = 100
            };

            var queryResult = LogQueryFS.Query(filter);
            Console.WriteLine($"TotalCount={queryResult.TotalCount}, Entries.Count={queryResult.Entries.Count}");
            Console.WriteLine($"MatchedCategories: {string.Join(", ", queryResult.MatchedCategories)}");

            Console.WriteLine("注意：NonExistent/Category 路径不存在，ResolvePathToFlags 返回空列表");
            Console.WriteLine("Query 方法中 bitFlags.Count > 0 为 false，跳过分类过滤");
            Console.WriteLine($"所以实际返回了 {queryResult.Entries.Count} 条日志（所有日志）");
        }

        [TestMethod]
        public void Diag_QueryFS_LimitAndHasMore()
        {
            LogRepository.Reset();
            var logger = new Common.Diagnostics.Logger(moduleName: "TestModule")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };

            for (int i = 0; i < 10; i++)
            {
                logger.Info(LogCategory.System, $"Message {i}");
            }

            var filter = new LogQueryFilter
            {
                Limit = 3
            };

            var queryResult = LogQueryFS.Query(filter);
            Console.WriteLine($"TotalCount={queryResult.TotalCount}, Entries.Count={queryResult.Entries.Count}, HasMore={queryResult.HasMore}");
            Console.WriteLine($"filter.Limit={filter.Limit}");
            Console.WriteLine($"sortedEntries.Count > filter.Limit = {queryResult.TotalCount} > {filter.Limit} = {queryResult.TotalCount > filter.Limit}");

            foreach (var e in queryResult.Entries)
            {
                Console.WriteLine($"  [{e.Timestamp:HH:mm:ss.fff}] {e.Message}");
            }

            Assert.AreEqual(3, queryResult.Entries.Count, "Limit=3 应该只返回 3 条");
            Assert.IsTrue(queryResult.HasMore, "10 条日志 Limit=3，HasMore 应该为 true");
        }
    }
}
