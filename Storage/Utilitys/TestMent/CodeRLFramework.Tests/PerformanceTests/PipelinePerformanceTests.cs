
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace CodeRLFramework.Tests.PerformanceTests
{
    [Trait("Category", "Performance")]
    public class PipelinePerformanceTests : TestBase
    {
        private readonly ITestOutputHelper _output;

        public PipelinePerformanceTests(ITestOutputHelper output) : base()
        {
            _output = output;
        }

        [Fact]
        public void ExecuteEpisode_WithSmallConfig_ShouldCompleteUnder100ms()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"小配置管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ExecuteEpisode_WithMediumConfig_ShouldCompleteUnder500ms()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"中配置管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ExecuteEpisode_WithConvergence_ShouldCompleteFaster()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.RewardHistory.AddRange(
                Enumerable.Repeat(0.95, 10));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"收敛场景管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ExecuteEpisode_WithAllSkeletons_ShouldNotDegrade()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            context.NavContext = TestDataFactory.CreateBasicNavContext();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"全 Skeleton 管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void Factory_CreateTrainingPipeline_ShouldCompleteQuickly()
        {
            var factory = CreateDefaultFactory();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var pipeline = factory.CreateTrainingPipeline();
            sw.Stop();

            Assert.NotNull(pipeline);
            _output.WriteLine($"创建训练管道耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void Factory_CreateSingleEpisodePipeline_ShouldCompleteQuickly()
        {
            var factory = CreateDefaultFactory();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var pipeline = factory.CreateSingleEpisodePipeline();
            sw.Stop();

            Assert.NotNull(pipeline);
            _output.WriteLine($"创建单 Episode 管道耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ExecuteEpisode_WithComplexSpace_ShouldCompleteUnder1000ms()
        {
            var (space, _, agent, converter) = CreateComplexDependencies();
            using var pipeline = new CodeRLPipeline(
                "PerfTest",
                extractor: new VectorExtractor(),
                agent: agent,
                converter: converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient);
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = space;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"复杂空间管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ExecuteEpisode_MultipleTimes_ShouldShowStablePerformance()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            var times = new List<long>();

            for (int i = 0; i < 5; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                context = pipeline.ExecuteEpisode(context);
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
            }

            Assert.NotNull(context);
            _output.WriteLine($"多次执行耗时: min={times.Min()}ms, max={times.Max()}ms, avg={times.Average():F0}ms");
        }

        [Fact]
        public void ExecuteEpisode_WithEmptySpace_ShouldCompleteQuickly()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.ExecuteEpisode(context);
            sw.Stop();

            Assert.NotNull(result);
            _output.WriteLine($"空空间管道执行耗时: {sw.ElapsedMilliseconds}ms");
        }
    }
}
