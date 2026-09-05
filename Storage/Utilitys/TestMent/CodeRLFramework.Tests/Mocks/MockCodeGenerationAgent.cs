
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.VectorSpace;

namespace CodeRLFramework.Tests.Mocks
{
    public class MockCodeGenerationAgent
    {
        private readonly string _predefinedCode;
        private readonly int _predefinedAction;
        private readonly CodeTokenizer _tokenizer;

        public CodeGenerationAgent InnerAgent { get; }

        public MockCodeGenerationAgent(
            CodeTokenizer? tokenizer = null,
            string predefinedCode = "public void Test() { Initialize(); Process(1); Finalize(); }",
            int predefinedAction = 0)
        {
            _tokenizer = tokenizer ?? new CodeTokenizer(new FunctionVectorSpace());
            _predefinedCode = predefinedCode;
            _predefinedAction = predefinedAction;
            InnerAgent = new CodeGenerationAgent(_tokenizer);
        }

        public bool GenerateCalled { get; private set; }

        public bool SelectActionCalled { get; private set; }

        public int[]? LastContextTokens { get; private set; }

        public double[]? LastLogits { get; private set; }

        public CodeTokenizer Tokenizer => _tokenizer;

        public int[] Generate(int[] contextTokens, int maxSteps = 50, double temperature = 1.0)
        {
            GenerateCalled = true;
            LastContextTokens = contextTokens;
            return InnerAgent.Generate(contextTokens, maxSteps, temperature);
        }

        public int SelectAction(double[] logits, double temperature = 1.0, double epsilon = 0.1)
        {
            SelectActionCalled = true;
            LastLogits = logits;
            return _predefinedAction;
        }
    }
}
