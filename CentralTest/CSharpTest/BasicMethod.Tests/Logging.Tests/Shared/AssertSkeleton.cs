
using CentralConsole.Testing.Assertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests.Shared
{
    public static class AssertSkeleton
    {
        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Action assertion)
            where TContext : class
            => AssertionSkeleton.Check<TContext>(name, assertion);

        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Func<bool> predicate)
            where TContext : class
            => AssertionSkeleton.Check<TContext>(name, predicate);

        public static ISkeleton<TContext, TContext> Check<TContext>(
            string name, Action<TContext> assertion)
            where TContext : class
            => AssertionSkeleton.Check<TContext>(name, assertion);

        public static ISkeleton<Unit, bool> IsTrue(string name, Func<bool> predicate)
        {
            return new DelegateSkeleton<Unit, bool>(
                $"Assert:{name}",
                syncWithCtx: (_, _) =>
                {
                    Assert.IsTrue(predicate(), name);
                    return true;
                });
        }
    }
}
