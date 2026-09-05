
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogQueryFilterTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
        }

        [TestMethod]
        public void Test_Filter_DefaultConstructor()
        {
            var result = RunTestGroup("LogQueryFilter 默认构造函数",
                ("默认构造函数 Path 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Path == null;
                })),
                ("默认构造函数 Level 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Level == null;
                })),
                ("默认构造函数 Since 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Since == null;
                })),
                ("默认构造函数 Keyword 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Keyword == null;
                })),
                ("默认构造函数 Limit 为 100", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Limit == 100;
                })),
                ("默认构造函数 Recursive 为 true", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.Recursive == true;
                })),
                ("默认构造函数 FromTime 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.FromTime == null;
                })),
                ("默认构造函数 ToTime 为 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.ToTime == null;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Filter_PropertySetters()
        {
            var result = RunTestGroup("LogQueryFilter 属性设置",
                ("设置所有属性后读取正确", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter
                    {
                        Path = "Module/Cat1",
                        Level = "ERROR",
                        Since = "1h",
                        Limit = 50,
                        Keyword = "test",
                        Recursive = false,
                        FromTime = new DateTime(2024, 1, 1),
                        ToTime = new DateTime(2024, 12, 31)
                    };

                    return filter.Path == "Module/Cat1"
                        && filter.Level == "ERROR"
                        && filter.Since == "1h"
                        && filter.Limit == 50
                        && filter.Keyword == "test"
                        && filter.Recursive == false
                        && filter.FromTime == new DateTime(2024, 1, 1)
                        && filter.ToTime == new DateTime(2024, 12, 31);
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Filter_GetFromTime_Relative()
        {
            var result = RunTestGroup("LogQueryFilter GetFromTime 相对时间解析",
                ("Since=5m 返回 5 分钟前", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "5m" };
                    var fromTime = filter.GetFromTime();
                    return fromTime.HasValue
                        && Math.Abs((DateTime.UtcNow - fromTime.Value).TotalMinutes - 5) < 1;
                })),
                ("Since=1h 返回 1 小时前", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "1h" };
                    var fromTime = filter.GetFromTime();
                    return fromTime.HasValue
                        && Math.Abs((DateTime.UtcNow - fromTime.Value).TotalHours - 1) < 0.1;
                })),
                ("Since=30s 返回 30 秒前", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "30s" };
                    var fromTime = filter.GetFromTime();
                    return fromTime.HasValue
                        && Math.Abs((DateTime.UtcNow - fromTime.Value).TotalSeconds - 30) < 1;
                })),
                ("Since=2d 返回 2 天前", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "2d" };
                    var fromTime = filter.GetFromTime();
                    return fromTime.HasValue
                        && Math.Abs((DateTime.UtcNow - fromTime.Value).TotalDays - 2) < 0.1;
                })),
                ("Since 不区分大小写", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "5M" };
                    var fromTime = filter.GetFromTime();
                    return fromTime.HasValue;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Filter_GetFromTime_Absolute()
        {
            var result = RunTestGroup("LogQueryFilter GetFromTime 绝对时间",
                ("FromTime 优先于 Since", new Func<bool>(() =>
                {
                    var absoluteTime = new DateTime(2024, 6, 15, 10, 30, 0);
                    var filter = new LogQueryFilter
                    {
                        FromTime = absoluteTime,
                        Since = "1h"
                    };

                    var fromTime = filter.GetFromTime();
                    return fromTime == absoluteTime;
                }))
            );

            AssertAllPassed(result);
        }


        [TestMethod]
        public void Test_Filter_GetFromTime_Empty()
        {
            var result = RunTestGroup("LogQueryFilter GetFromTime 空/无效时间",
                ("Since 为 null 返回 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter();
                    return filter.GetFromTime() == null;
                })),
                ("Since 为空字符串返回 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "" };
                    return filter.GetFromTime() == null;
                })),
                ("Since 为空白字符串返回 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "   " };
                    return filter.GetFromTime() == null;
                })),
                ("Since 为无效格式返回 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "invalid" };
                    return filter.GetFromTime() == null;
                })),
                ("Since 为数字无单位返回 null", new Func<bool>(() =>
                {
                    var filter = new LogQueryFilter { Since = "123" };
                    return filter.GetFromTime() == null;
                }))
            );

            AssertAllPassed(result);
        }
    }
}
