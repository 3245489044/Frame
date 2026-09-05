
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestInfrastructure
{
    public static class AssertionHelper
    {
        public static void AssertAllPassed(this TestGroupResult result)
        {
            if (!result.AllPassed)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"测试组 [{result.GroupName}] 有 {result.Failed}/{result.Total} 个测试失败:");
                foreach (var msg in result.Messages.Where(m => m.StartsWith("[FAIL") || m.StartsWith("[ERROR")))
                {
                    sb.AppendLine($"  {msg}");
                }
                Assert.Fail(sb.ToString());
            }
        }

        public static void AssertAllPassed(this TestGroupResult result, bool printSummary)
        {
            if (printSummary)
            {
                PrintSummary(result);
            }
            AssertAllPassed(result);
        }

        public static void AssertPassRate(this TestGroupResult result, double minPassRate)
        {
            if (result.PassRate < minPassRate)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"测试组 [{result.GroupName}] 通过率 {result.PassRate:F2}% 低于要求 {minPassRate}%");
                sb.AppendLine($"  通过: {result.Passed}/{result.Total}");
                foreach (var msg in result.Messages.Where(m => m.StartsWith("[FAIL") || m.StartsWith("[ERROR")))
                {
                    sb.AppendLine($"  {msg}");
                }
                Assert.Fail(sb.ToString());
            }
        }

        public static void AssertNoFailures(this TestGroupResult result)
        {
            AssertAllPassed(result);
        }

        public static List<string> GetFailedMessages(this TestGroupResult result)
        {
            return result.Messages.Where(m => m.StartsWith("[FAIL") || m.StartsWith("[ERROR")).ToList();
        }

        public static List<string> GetPassedMessages(this TestGroupResult result)
        {
            return result.Messages.Where(m => m.StartsWith("[PASS")).ToList();
        }

        public static void PrintSummary(this TestGroupResult result)
        {
            Console.WriteLine($"========================================");
            Console.WriteLine($"  测试组: {result.GroupName}");
            Console.WriteLine($"========================================");
            Console.WriteLine($"  总测试数: {result.Total}");
            Console.WriteLine($"  通过: {result.Passed}");
            Console.WriteLine($"  失败: {result.Failed}");
            Console.WriteLine($"  跳过: {result.Skipped}");
            Console.WriteLine($"  通过率: {result.PassRate:F2}%");
            Console.WriteLine($"  耗时: {result.Duration.TotalSeconds:F2}s");
            Console.WriteLine($"========================================");

            if (!result.AllPassed)
            {
                Console.WriteLine("失败/错误的测试:");
                foreach (var msg in GetFailedMessages(result))
                {
                    Console.WriteLine($"  {msg}");
                }
            }
            Console.WriteLine();
        }
    }

    public static class AssertionSkeleton
    {
        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Action assertion)
            where TContext : class
        {
            return new DelegateSkeleton<TContext, TContext>(
                $"Assert:{name}",
                syncWithCtx: (ctx, _) =>
                {
                    assertion();
                    return ctx;
                });
        }

        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Func<bool> predicate)
            where TContext : class
        {
            return new DelegateSkeleton<TContext, TContext>(
                $"Assert:{name}",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsTrue(predicate(), name);
                    return ctx;
                });
        }

        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Action<TContext> assertion)
            where TContext : class
        {
            return new DelegateSkeleton<TContext, TContext>(
                $"Assert:{name}",
                syncWithCtx: (ctx, _) =>
                {
                    assertion(ctx);
                    return ctx;
                });
        }
    }
}
