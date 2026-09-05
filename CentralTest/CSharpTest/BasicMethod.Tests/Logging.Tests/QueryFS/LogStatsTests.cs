
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogStatsTests : LoggingTestBase<Unit>
    {

        [TestMethod]
        public void Test_LogStats_DefaultConstructor()
        {
            var result = RunTestGroup("LogStats 默认构造函数",
                ("默认构造函数 Path 为空", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.Path == string.Empty;
                })),
                ("默认构造函数 TotalCount 为 0", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.TotalCount == 0;
                })),
                ("默认构造函数 ByLevel 为空字典", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.ByLevel.Count == 0;
                })),
                ("默认构造函数 Children 为空字典", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.Children.Count == 0;
                })),
                ("默认构造函数 FirstLogTime 为 null", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.FirstLogTime == null;
                })),
                ("默认构造函数 LastLogTime 为 null", new Func<bool>(() =>
                {
                    var stats = new LogStats();
                    return stats.LastLogTime == null;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_LogStats_ToFormattedText()
        {
            var result = RunTestGroup("LogStats ToFormattedText",
                ("基本统计信息格式化", new Func<bool>(() =>
                {
                    var stats = new LogStats
                    {
                        Path = "Module/Cat1",
                        TotalCount = 42
                    };
                    stats.ByLevel["INFO"] = 20;
                    stats.ByLevel["ERROR"] = 22;

                    var text = stats.ToFormattedText();
                    return text.Contains("Module/Cat1")
                        && text.Contains("42")
                        && text.Contains("INFO=20")
                        && text.Contains("ERROR=22");
                })),
                ("包含时间范围", new Func<bool>(() =>
                {
                    var stats = new LogStats
                    {
                        Path = "Module/Cat1",
                        TotalCount = 5,
                        FirstLogTime = new DateTime(2024, 1, 1, 10, 0, 0),
                        LastLogTime = new DateTime(2024, 1, 1, 12, 0, 0)
                    };

                    var text = stats.ToFormattedText();
                    return text.Contains("From:")
                        && text.Contains("To:");
                })),
                ("无时间范围不显示", new Func<bool>(() =>
                {
                    var stats = new LogStats
                    {
                        Path = "Module/Cat1",
                        TotalCount = 0
                    };

                    var text = stats.ToFormattedText();
                    return !text.Contains("From:")
                        && !text.Contains("To:");
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_LogStats_Children()
        {
            var result = RunTestGroup("LogStats Children 子分类统计",
                ("子分类统计可嵌套", new Func<bool>(() =>
                {
                    var stats = new LogStats
                    {
                        Path = "Module",
                        TotalCount = 10
                    };

                    var childStats = new LogStats
                    {
                        Path = "Module/Sub",
                        TotalCount = 5
                    };
                    childStats.ByLevel["INFO"] = 5;

                    stats.Children["Sub"] = childStats;

                    return stats.Children.Count == 1
                        && stats.Children["Sub"].TotalCount == 5
                        && stats.Children["Sub"].ByLevel["INFO"] == 5;
                }))
            );

            AssertAllPassed(result);
        }
    }
}
