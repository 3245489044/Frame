
using CodeRLFramework.VectorSpace;
using WorkFlowMethod.GraphFramework.Snapshots;

namespace CodeRLFramework.Tests.Mocks
{
    public class MockVectorExtractor
    {
        private readonly FunctionVectorSpace? _predefinedSpace;
        private readonly GraphSnapshot? _predefinedSnapshot;

        public VectorExtractor InnerExtractor { get; }

        public MockVectorExtractor(
            FunctionVectorSpace? predefinedSpace = null,
            GraphSnapshot? predefinedSnapshot = null)
        {
            _predefinedSpace = predefinedSpace;
            _predefinedSnapshot = predefinedSnapshot;
            InnerExtractor = new VectorExtractor();
        }

        public bool ExtractFromCodeCalled { get; private set; }

        public bool ExtractFromMultipleCalled { get; private set; }

        public FunctionVectorSpace ExtractFromCode(string code, string source = "codebase")
        {
            ExtractFromCodeCalled = true;
            return _predefinedSpace ?? InnerExtractor.ExtractFromCode(code, source);
        }

        public FunctionVectorSpace ExtractFromMultiple(List<string> codes, string source = "codebase")
        {
            ExtractFromMultipleCalled = true;
            return _predefinedSpace ?? InnerExtractor.ExtractFromMultiple(codes, source);
        }

        public FunctionVectorSpace? PredefinedSpace => _predefinedSpace;
    }
}
