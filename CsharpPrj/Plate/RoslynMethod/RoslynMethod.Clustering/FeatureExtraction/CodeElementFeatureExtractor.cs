namespace MathematicalAlgorithms
{
    public class CodeElementFeatureExtractor
    {
        public double[] ExtractFeatures(EnhancedCodeElement element)
        {
            if (element == null)
                return Array.Empty<double>();

            return new double[]
            {
                Normalize(element.ComplexityScore, 50),
                Normalize(element.LinesOfCode, 500),
                Normalize(element.ReferenceCount, 100),
                Normalize(element.VariableCount, 30),
                Normalize(element.ParameterCount, 10),
                element.IsStatic ? 1 : 0,
                element.IsPure ? 1 : 0,
                element.HasSideEffects ? 1 : 0,
                Normalize(element.CommentLines, 100),
                Normalize(element.BodyLines, 300),
                Normalize(element.VariableReuseRatio, 1),
                Normalize(element.VariableScopeComplexity, 10)
            };
        }

        private static double Normalize(double value, double max)
        {
            return max > 0 ? Math.Min(value / max, 1.0) : 0;
        }
    }
}
