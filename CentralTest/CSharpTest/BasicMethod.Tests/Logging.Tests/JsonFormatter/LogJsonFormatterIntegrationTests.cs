
using System.Text.Json;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using Logger = Common.Diagnostics.Logger;

namespace Logging.Tests
{
    [TestClass]
    public class LogJsonFormatterIntegrationTests : LoggingIntegrationTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogRepository.Reset();
        }

        private static void WriteTestLogs()
        {
            var wdUiLogger = new Common.Diagnostics.Logger(moduleName: "WdUI")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };
            var fdLogger = new Common.Diagnostics.Logger(moduleName: "FileDriven")
            {
                EnabledCategories = LogCategory.All,
                ActiveTargets = LogOutputTarget.Repository
            };

            wdUiLogger.Info(LogCategories.WdUI_Layout, "Layout calculation started");
            wdUiLogger.Error(LogCategories.WdUI_Render, "Render failed: out of memory");
            fdLogger.Info(LogCategories.FileDriven_General, "File loaded successfully");
            fdLogger.Error(LogCategories.FileDriven_Command, "Command failed: file not found");
        }

        [TestMethod]
        public void Test_JsonFormatter_CategoriesJson()
        {
            var result = RunTestGroup("LogJsonFormatter FormatCategoriesJson",
                ("返回有效的 JSON 字符串", new Func<bool>(() =>
                {
                    var json = LogJsonFormatter.FormatCategoriesJson();

                    Assert.IsFalse(string.IsNullOrEmpty(json), "JSON should not be null or empty");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    return root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("count", out _)
                        && root.TryGetProperty("categories", out _);
                })),
                ("每个分类包含 path/description/category", new Func<bool>(() =>
                {
                    var json = LogJsonFormatter.FormatCategoriesJson();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var categories = root.GetProperty("categories");

                    var first = categories[0];
                    return first.TryGetProperty("path", out _)
                        && first.TryGetProperty("description", out _)
                        && first.TryGetProperty("category", out _);
                })),
                ("包含 WdUI 和 FileDriven 分类", new Func<bool>(() =>
                {
                    var json = LogJsonFormatter.FormatCategoriesJson();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var categories = root.GetProperty("categories");

                    var paths = new List<string>();
                    foreach (var item in categories.EnumerateArray())
                    {
                        paths.Add(item.GetProperty("path").GetString()!);
                    }

                    return paths.Any(p => p.StartsWith("WdUI"))
                        && paths.Any(p => p.StartsWith("FileDriven"));
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_JsonFormatter_QueryToJson()
        {
            var result = RunTestGroup("LogJsonFormatter QueryToJson",
                ("按路径查询返回 JSON", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var json = LogJsonFormatter.QueryToJson(
                        path: "WdUI", level: null, since: null, limit: 100, keyword: null, compact: false);

                    Assert.IsFalse(string.IsNullOrEmpty(json));

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    return root.TryGetProperty("queryPath", out _)
                        && root.TryGetProperty("totalCount", out _)
                        && root.TryGetProperty("returnedCount", out _)
                        && root.TryGetProperty("entries", out _);
                })),
                ("totalCount 和 returnedCount 正确", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var json = LogJsonFormatter.QueryToJson(
                        path: "WdUI", level: null, since: null, limit: 100, keyword: null, compact: false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    return root.GetProperty("totalCount").GetInt32() == 2
                        && root.GetProperty("returnedCount").GetInt32() == 2;
                })),
                ("hasMore 标志正确", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var json = LogJsonFormatter.QueryToJson(
                        path: "WdUI", level: null, since: null, limit: 1, keyword: null, compact: false);
                    using var doc = JsonDocument.Parse(json);

                    return doc.RootElement.GetProperty("hasMore").GetBoolean();
                })),
                ("按路径+级别查询返回 JSON", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var json = LogJsonFormatter.QueryToJson(
                        path: "WdUI", level: "ERROR", since: null, limit: 100, keyword: null, compact: false);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    return root.GetProperty("totalCount").GetInt32() == 1;
                })),
                ("按关键词查询返回 JSON", new Func<bool>(() =>
                {
                    LogRepository.Reset();
                    WriteTestLogs();

                    var json = LogJsonFormatter.QueryToJson(
                        path: null, level: null, since: null, limit: 100, keyword: "failed", compact: false);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    return root.GetProperty("totalCount").GetInt32() == 2;
                }))
            );

            AssertAllPassed(result);
        }

        [TestMethod]
        public void Test_JsonFormatter_EmptyEntries()
        {
            var result = RunTestGroup("LogJsonFormatter 空日志条目",
                ("空仓库查询结果 totalCount=0", new Func<bool>(() =>
                {
                    LogRepository.Reset();

                    var json = LogJsonFormatter.QueryToJson(
                        path: "WdUI", level: null, since: null, limit: 100, keyword: null, compact: false);

                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("totalCount").GetInt32() == 0;
                }))
            );

            AssertAllPassed(result);
        }
    }
}
