
using CodeRLFramework.Generation;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Generation
{
    public class CodeGenerationAgentTests
    {
        [Fact]
        public void Constructor_WithNullTokenizer_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeGenerationAgent(null!));
        }

        [Fact]
        public void Constructor_WithValidTokenizer_ShouldSucceed()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);

            var agent = new CodeGenerationAgent(tokenizer);

            Assert.NotNull(agent);
        }

        [Fact]
        public void GenerateCode_WithValidPrompt_ShouldReturnCode()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);

            var code = agent.GenerateCode("Generate a test method");

            Assert.NotNull(code);
            Assert.NotEmpty(code);
        }

        [Fact]
        public void GenerateCode_WithEmptyPrompt_ShouldReturnCode()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);

            var code = agent.GenerateCode("");

            Assert.NotNull(code);
        }

        [Fact]
        public void SelectAction_WithValidState_ShouldReturnAction()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);
            var state = TestDataFactory.GenerateTestVector(0);

            var action = agent.SelectAction(state);

            Assert.InRange(action, 0, int.MaxValue);
        }

        [Fact]
        public void SelectAction_WithNullState_ShouldThrow()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);

            Assert.Throws<ArgumentNullException>(() =>
                agent.SelectAction(null!));
        }

        [Fact]
        public void NavigateGraph_WithValidContext_ShouldReturnContext()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);
            var navContext = TestDataFactory.CreateBasicNavContext();
            var availableNodes = new List<string> { "Initialize", "Process", "Finalize" };

            var result = agent.NavigateGraph(navContext, availableNodes);

            Assert.NotNull(result);
        }

        [Fact]
        public void NavigateGraph_WithNullContext_ShouldThrow()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var agent = new CodeGenerationAgent(tokenizer);

            Assert.Throws<ArgumentNullException>(() =>
                agent.NavigateGraph(null!, new List<string>()));
        }
    }
}
