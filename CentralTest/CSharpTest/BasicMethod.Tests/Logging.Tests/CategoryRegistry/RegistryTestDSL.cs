
using Common.Diagnostics;
using Logging.Tests.Shared;
using SkeletonFlow;

namespace Logging.Tests
{
    public abstract class RegistryTestBase : LoggingTestBase<RegistryFixtureContext>
    {
        protected override void CleanupTest()
        {
            LoggingTestCleanup.CleanupCategoryRegistry();
        }


        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertEach(
            string nameTemplate,
            Func<string, string, ISkeleton<RegistryFixtureContext, RegistryFixtureContext>> assertFactory,
            params string[] paths)
        {
            ISkeleton<RegistryFixtureContext, RegistryFixtureContext> chain = null!;
            foreach (var path in paths)
            {
                var assert = assertFactory(string.Format(nameTemplate, path), path);
                chain = chain == null ? assert : chain.Then(assert);
            }
            return chain!;
        }

        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertAllRegistered(
            params string[] paths) =>
            AssertEach("已注册: {0}", RegistryAssert.IsRegistered, paths);

        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertAllFlatMapContains(
            params string[] paths) =>
            AssertEach("FlatMap 包含: {0}", RegistryAssert.TreeFlatMapContains, paths);

        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertAllPathsContain(
            params string[] paths) =>
            AssertEach("路径包含: {0}", RegistryAssert.PathsContain, paths);

        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertAllNodesExist(
            params string[] paths) =>
            AssertEach("节点存在: {0}", RegistryAssert.NodeExists, paths);

        protected static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AssertAllBitFlagsNonZero(
            params string[] paths) =>
            AssertEach("位标志非零: {0}", RegistryAssert.BitFlagNonZero, paths);


        protected static ISkeleton<Unit, bool> ClearThen(
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.Clear, assertions);

        protected static ISkeleton<Unit, bool> RegisterThen(string path, string desc,
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.Register(path, desc), assertions);

        protected static ISkeleton<Unit, bool> RegisterRangeThen(
            (string path, string desc)[] entries,
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.RegisterRange(entries), assertions);

        protected static ISkeleton<Unit, bool> InitDefaultsThen(
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.InitDefaults, assertions);

        protected static ISkeleton<Unit, bool> RegisterModuleThen(string moduleName,
            (string name, string desc)[] features,
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.RegisterModule(moduleName, features), assertions);

        protected static ISkeleton<Unit, bool> RegisterTreeThen(Dictionary<string, object> treeDef,
            params ISkeleton<RegistryFixtureContext, RegistryFixtureContext>[] assertions) =>
            TestFlow(RegistryFixture.RegisterTree(treeDef), assertions);
    }
}
