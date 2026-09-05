namespace CodeRLFramework.Generation
{
    public class OutputVectorSpace
    {
        public List<OutputVector> Vectors { get; set; } = new();

        public double Confidence { get; set; }

        public double Temperature { get; set; }

        public List<FunctionTrace> FunctionTrace { get; set; } = new();
    }

    public class OutputVector
    {
        public string FunctionId { get; set; } = string.Empty;

        public string FunctionName { get; set; } = string.Empty;

        public double[]? Vector { get; set; }

        public double Probability { get; set; }

        public string Source { get; set; } = string.Empty;

        public double SourceWeight { get; set; }

        public Dictionary<string, string> ParameterBindings { get; set; } = new();
    }

    public class FunctionTrace
    {
        public string FunctionId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public double Weight { get; set; }
    }
}
