
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogQueryResultTests : LoggingTestBase<Unit>
    {

        [TestMethod]
        public void Test_QueryResult_DefaultConstructor()
        {
            var result = RunTestGroup("LogQueryResult 默认构造函数",
                ("默认构造函数 QueryPath 为空", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    return qr.QueryPath == string.Empty;
                })),
                ("默认构造函数 MatchedCategories 为空列表", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    return qr.MatchedCategories.Count == 0;
                })),
                ("默认构造函数 Entries 为空列表", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    return qr.Entries.Count == 0;
                })),
                ("默认构造函数 TotalCount 为 0", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    return qr.TotalCount == 0;
                })),
                ("默认构造函数 HasMore 为 false", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    return qr.HasMore == false;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryResult_ToFormattedText()
        {
            var result = RunTestGroup("LogQueryResult ToFormattedText",
                ("空结果返回查询信息", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult
                    {
                        QueryPath = "Module/Cat1",
                        TotalCount = 0
                    };
                    qr.MatchedCategories.Add("Module/Cat1");

                    var text = qr.ToFormattedText();
                    return text.Contains("Module/Cat1")
                        && text.Contains("Total: 0");
                })),
                ("包含日志条目", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult
                    {
                        QueryPath = "Module/Cat1",
                        TotalCount = 1
                    };
                    qr.MatchedCategories.Add("Module/Cat1");
                    qr.Entries.Add(new LogEntry
                    {
                        Level = "ERROR",
                        Message = "test error",
                        Category = LogCategories.WdUI_Layout,
                        Timestamp = new DateTime(2024, 1, 1, 12, 0, 0)
                    });

                    var text = qr.ToFormattedText();
                    return text.Contains("ERROR")
                        && text.Contains("test error")
                        && text.Contains("WdUI_Layout");
                })),
                ("包含异常信息", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult
                    {
                        QueryPath = "Module/Cat1",
                        TotalCount = 1
                    };
                    qr.MatchedCategories.Add("Module/Cat1");
                    qr.Entries.Add(new LogEntry
                    {
                        Level = "ERROR",
                        Message = "error with exception",
                        Category = LogCategories.WdUI_Layout,
                        Exception = new InvalidOperationException("test exception")
                    });

                    var text = qr.ToFormattedText();
                    return text.Contains("Exception:")
                        && text.Contains("test exception");
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryResult_ToJson()
        {
            var result = RunTestGroup("LogQueryResult ToJson",
                ("空结果返回空 JSON 数组", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    var json = qr.ToJson();
                    return json.Trim() == "[]";
                })),
                ("包含日志条目", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    qr.Entries.Add(new LogEntry
                    {
                        Level = "INFO",
                        Message = "test message",
                        Category = LogCategories.WdUI_Render,
                        Timestamp = new DateTime(2024, 1, 1, 10, 30, 0)
                    });

                    var json = qr.ToJson();
                    return json.Contains("INFO")
                        && json.Contains("test message")
                        && json.Contains("WdUI_Render")
                        && json.Contains("timestamp");
                })),
                ("包含异常信息", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    qr.Entries.Add(new LogEntry
                    {
                        Level = "ERROR",
                        Message = "error msg",
                        Category = LogCategories.WdUI_Layout,
                        Exception = new InvalidOperationException("exception detail")
                    });

                    var json = qr.ToJson();
                    return json.Contains("exception")
                        && json.Contains("exception detail");
                })),
                ("特殊字符转义", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult();
                    qr.Entries.Add(new LogEntry
                    {
                        Level = "INFO",
                        Message = "line1\nline2\ttab\"quote\\backslash",
                        Category = LogCategories.WdUI_Render
                    });

                    var json = qr.ToJson();
                    return json.Contains("\\n")
                        && json.Contains("\\t")
                        && json.Contains("\\\"")
                        && json.Contains("\\\\");
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_QueryResult_HasMore()
        {
            var result = RunTestGroup("LogQueryResult HasMore",
                ("HasMore 为 true 时格式化文本包含提示", new Func<bool>(() =>
                {
                    var qr = new LogQueryResult
                    {
                        QueryPath = "Module/Cat1",
                        TotalCount = 150,
                        HasMore = true
                    };

                    var text = qr.ToFormattedText();
                    return text.Contains("HasMore") || text.Contains("Returned");
                }))
            );

            AssertAllPassed(result);
        }
    }
}
