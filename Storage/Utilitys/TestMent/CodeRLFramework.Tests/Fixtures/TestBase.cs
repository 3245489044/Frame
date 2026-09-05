
using CodeRLFramework.Generation;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;

namespace CodeRLFramework.Tests.Fixtures
{
    public abstract class TestBase : IDisposable
    {
        public FunctionVectorSpace FunctionSpace { get; }

        public CodeTokenizer Tokenizer { get; }

        public CodeGenerationAgent Agent { get; }

        public VectorToCodeConverter Converter { get; }

        public RewardFunction RewardFunction { get; }

        public PolicyGradient PolicyGradient { get; }

        public const string DefaultPipelineName = "TestPipeline";

        public TestParameterResolver ParameterResolver { get; }

        protected TestBase()
        {
            ParameterResolver = new TestParameterResolver();
            FunctionSpace = TestDataFactory.CreateBasicFunctionSpace();
            Tokenizer = new CodeTokenizer(FunctionSpace);
            Agent = new CodeGenerationAgent(Tokenizer);
            Converter = new VectorToCodeConverter(FunctionSpace, Tokenizer);
            RewardFunction = new RewardFunction();
            PolicyGradient = new PolicyGradient(
                ParameterResolver.GetDefaultActionCount());
        }

        protected CodeRLPipeline CreateDefaultPipeline()
        {
            return new CodeRLPipeline(
                DefaultPipelineName,
                extractor: new VectorExtractor(),
                agent: Agent,
                converter: Converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient);
        }

        protected CodeRLPipelineFactory CreateDefaultFactory()
        {
            return new CodeRLPipelineFactory(
                extractor: new VectorExtractor(),
                agent: Agent,
                converter: Converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient,
                functionSpace: FunctionSpace,
                tokenizer: Tokenizer,
                actionCount: ParameterResolver.GetDefaultActionCount());
        }

        protected (FunctionVectorSpace space, CodeTokenizer tokenizer, CodeGenerationAgent agent, VectorToCodeConverter converter) CreateComplexDependencies()
        {
            var space = TestDataFactory.CreateComplexFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);
            var converter = new VectorToCodeConverter(space, tokenizer);
            return (space, tokenizer, agent, converter);
        }

        protected (FunctionVectorSpace space, CodeTokenizer tokenizer, CodeGenerationAgent agent, VectorToCodeConverter converter) CreateEmptyDependencies()
        {
            var space = TestDataFactory.CreateEmptyFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);
            var converter = new VectorToCodeConverter(space, tokenizer);
            return (space, tokenizer, agent, converter);
        }

        protected CodeRLContext CreateParameterizedContext()
        {
            return ParameterResolver.CreateCodeRLContext();
        }

        protected CodeRLContext CreateParameterizedContextWithTokens()
        {
            return ParameterResolver.CreateContextWithTokens();
        }

        protected CodeRLContext CreateParameterizedContextWithGeneratedCode()
        {
            return ParameterResolver.CreateContextWithGeneratedCode();
        }

        protected CodeRLContext CreateParameterizedContextWithRewardHistory()
        {
            return ParameterResolver.CreateContextWithRewardHistory();
        }

        public void Dispose()
        {
            ParameterResolver.Dispose();
        }
    }
}


