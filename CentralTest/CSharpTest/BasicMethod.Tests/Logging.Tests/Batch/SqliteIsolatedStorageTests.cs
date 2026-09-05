
using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using SkeletonFlow.App;

namespace Logging.Tests.Batch
{
    [TestClass]
    public class SqliteIsolatedStorageTests : BatchTestBase
    {
        private static string GetTempDbPath()
        {
            return Path.Combine(Path.GetTempPath(), $"batch_test_{Guid.NewGuid():N}.db");
        }

        private static SqliteIsolatedStorage CreateStorage()
        {
            var dbPath = GetTempDbPath();
            var storage = new SqliteIsolatedStorage(dbPath);
            storage.Initialize();
            return storage;
        }

        private static void CleanupStorage(SqliteIsolatedStorage storage)
        {
            storage.Close();
            storage.Dispose();
        }

        #region 初始化测试

        [TestMethod]
        public void Test_Storage_Initialize()
        {
            RunTestGroup("初始化测试", builder =>
            {
                builder.Add("初始化后表存在", () =>
                {
                    var storage = CreateStorage();
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 0;
                });

                builder.Add("重复初始化幂等", () =>
                {
                    var dbPath = GetTempDbPath();
                    var storage = new SqliteIsolatedStorage(dbPath);
                    storage.Initialize();
                    storage.Initialize();
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 0;
                });
            }).AssertAllPassed();
        }

        #endregion

        #region 存储与查询测试

        [TestMethod]
        public void Test_Storage_StoreAndGetAll()
        {
            RunTestGroup("存储与查询测试", builder =>
            {
                builder.Add("写入5条返回5条", () =>
                {
                    var storage = CreateStorage();
                    var entries = Enumerable.Range(0, 5).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now.AddSeconds(-5 + i),
                        Level = "INFO",
                        Module = "test",
                        Message = $"Test message #{i}",
                        Category = LogCategory.System
                    }).ToList();
                    storage.WriteBatch(entries);
                    var all = storage.Query(new LogQueryFilter { Limit = int.MaxValue }).ToList();
                    CleanupStorage(storage);
                    return all.Count == 5;
                });

                builder.Add("空集合存储", () =>
                {
                    var storage = CreateStorage();
                    storage.WriteBatch(new List<LogEntry>());
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 0;
                });

                builder.Add("大量数据100条", () =>
                {
                    var storage = CreateStorage();
                    var entries = Enumerable.Range(0, 100).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now.AddSeconds(-100 + i),
                        Level = i % 3 == 0 ? "ERROR" : i % 3 == 1 ? "WARN" : "INFO",
                        Module = "test",
                        Message = $"Bulk message #{i}",
                        Category = LogCategory.System
                    }).ToList();
                    storage.WriteBatch(entries);
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 100;
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Storage_Query()
        {
            RunTestGroup("查询测试", builder =>
            {
                builder.Add("按级别过滤ERROR", () =>
                {
                    var storage = CreateStorage();
                    var entries = new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "Info msg", Category = LogCategory.System },
                        new() { Timestamp = DateTime.Now, Level = "ERROR", Message = "Error msg", Category = LogCategory.System },
                        new() { Timestamp = DateTime.Now, Level = "WARN", Message = "Warn msg", Category = LogCategory.System }
                    };
                    storage.WriteBatch(entries);
                    var result = storage.Query(new LogQueryFilter { Level = "ERROR" }).ToList();
                    CleanupStorage(storage);
                    return result.Count == 1 && result[0].Level == "ERROR";
                });

                builder.Add("按关键词搜索", () =>
                {
                    var storage = CreateStorage();
                    var entries = new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "Hello World", Category = LogCategory.System },
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "Goodbye World", Category = LogCategory.System }
                    };
                    storage.WriteBatch(entries);
                    var result = storage.Query(new LogQueryFilter { Keyword = "Hello" }).ToList();
                    CleanupStorage(storage);
                    return result.Count == 1 && result[0].Message == "Hello World";
                });

                builder.Add("按时间范围过滤", () =>
                {
                    var storage = CreateStorage();
                    var now = DateTime.Now;
                    var entries = new List<LogEntry>
                    {
                        new() { Timestamp = now.AddMinutes(-10), Level = "INFO", Message = "Old", Category = LogCategory.System },
                        new() { Timestamp = now, Level = "INFO", Message = "New", Category = LogCategory.System }
                    };
                    storage.WriteBatch(entries);
                    var result = storage.Query(new LogQueryFilter
                    {
                        FromTime = now.AddMinutes(-5),
                        ToTime = now.AddMinutes(1)
                    }).ToList();
                    CleanupStorage(storage);
                    return result.Count == 1 && result[0].Message == "New";
                });

                builder.Add("组合过滤", () =>
                {
                    var storage = CreateStorage();
                    var entries = new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "ERROR", Module = "modA", Message = "Error in A", Category = LogCategory.System },
                        new() { Timestamp = DateTime.Now, Level = "ERROR", Module = "modB", Message = "Error in B", Category = LogCategory.System },
                        new() { Timestamp = DateTime.Now, Level = "INFO", Module = "modA", Message = "Info in A", Category = LogCategory.System }
                    };
                    storage.WriteBatch(entries);
                    var result = storage.Query(new LogQueryFilter
                    {
                        Level = "ERROR",
                        Keyword = "Error"
                    }).ToList();
                    CleanupStorage(storage);
                    return result.Count == 2;
                });
            }).AssertAllPassed();
        }

        #endregion

        #region 计数与清空测试

        [TestMethod]
        public void Test_Storage_Count()
        {
            RunTestGroup("计数测试", builder =>
            {
                builder.Add("计数正确", () =>
                {
                    var storage = CreateStorage();
                    var entries = Enumerable.Range(0, 10).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO", Message = $"Msg {i}", Category = LogCategory.System
                    }).ToList();
                    storage.WriteBatch(entries);
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 10;
                });

                builder.Add("清空后为0", () =>
                {
                    var storage = CreateStorage();
                    var entries = Enumerable.Range(0, 5).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO", Message = $"Msg {i}", Category = LogCategory.System
                    }).ToList();
                    storage.WriteBatch(entries);
                    storage.Clear();
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 0;
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Storage_Clear()
        {
            RunTestGroup("清空测试", builder =>
            {
                builder.Add("清空后GetAll为空", () =>
                {
                    var storage = CreateStorage();
                    var entries = Enumerable.Range(0, 5).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO", Message = $"Msg {i}", Category = LogCategory.System
                    }).ToList();
                    storage.WriteBatch(entries);
                    storage.Clear();
                    var all = storage.Query(new LogQueryFilter { Limit = int.MaxValue }).ToList();
                    CleanupStorage(storage);
                    return all.Count == 0;
                });
            }).AssertAllPassed();
        }

        #endregion

        #region 多批次隔离测试

        [TestMethod]
        public void Test_Storage_MultipleBatches_Isolated()
        {
            RunTestGroup("多批次隔离测试", builder =>
            {
                builder.Add("两个批次互不干扰", () =>
                {
                    var storageA = CreateStorage();
                    var storageB = CreateStorage();

                    storageA.WriteBatch(new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "A-only", Category = LogCategory.System }
                    });
                    storageB.WriteBatch(new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "B-only", Category = LogCategory.System }
                    });

                    var allA = storageA.Query(new LogQueryFilter { Limit = int.MaxValue }).ToList();
                    var allB = storageB.Query(new LogQueryFilter { Limit = int.MaxValue }).ToList();
                    CleanupStorage(storageA);
                    CleanupStorage(storageB);
                    return allA.Count == 1 && allB.Count == 1
                        && allA[0].Message == "A-only"
                        && allB[0].Message == "B-only";
                });

                builder.Add("各自计数正确", () =>
                {
                    var storageA = CreateStorage();
                    var storageB = CreateStorage();

                    storageA.WriteBatch(Enumerable.Range(0, 3).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO", Message = $"A-{i}", Category = LogCategory.System
                    }).ToList());
                    storageB.WriteBatch(Enumerable.Range(0, 7).Select(i => new LogEntry
                    {
                        Timestamp = DateTime.Now, Level = "INFO", Message = $"B-{i}", Category = LogCategory.System
                    }).ToList());

                    var countA = storageA.Count();
                    var countB = storageB.Count();
                    CleanupStorage(storageA);
                    CleanupStorage(storageB);
                    return countA == 3 && countB == 7;
                });
            }).AssertAllPassed();
        }

        #endregion

        #region 重复写入测试

        [TestMethod]
        public void Test_Storage_StoreDuplicate()
        {
            RunTestGroup("重复写入测试", builder =>
            {
                builder.Add("重复写入相同数据", () =>
                {
                    var storage = CreateStorage();
                    var entries = new List<LogEntry>
                    {
                        new() { Timestamp = DateTime.Now, Level = "INFO", Message = "Same", Category = LogCategory.System }
                    };
                    storage.WriteBatch(entries);
                    storage.WriteBatch(entries);
                    var count = storage.Count();
                    CleanupStorage(storage);
                    return count == 2;
                });
            }).AssertAllPassed();
        }

        #endregion
    }
}
