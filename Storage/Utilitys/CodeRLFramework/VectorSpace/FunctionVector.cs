namespace CodeRLFramework.VectorSpace
{
    public class FunctionVector
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Signature { get; set; } = string.Empty;

        public string ClassName { get; set; } = string.Empty;

        public string Namespace { get; set; } = string.Empty;

        public double[]? SemanticVector { get; set; }

        public double[]? StructuralVector { get; set; }

        public double[]? RelationVector { get; set; }

        public string ReturnType { get; set; } = string.Empty;

        public List<ParameterInfo> Parameters { get; set; } = new();

        public int ComplexityScore { get; set; }

        public bool IsPure { get; set; }

        public bool HasSideEffects { get; set; }

        public List<string> CalledBy { get; set; } = new();

        public List<string> Calls { get; set; } = new();

        public string SourceCode { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public double SourceWeight { get; set; } = 1.0;


        public double[] GetFullVector()
        {
            int semanticDim = SemanticVector?.Length ?? 0;
            int structuralDim = StructuralVector?.Length ?? 0;
            int relationDim = RelationVector?.Length ?? 0;

            var full = new double[semanticDim + structuralDim + relationDim];
            int offset = 0;

            if (SemanticVector != null)
            {
                Array.Copy(SemanticVector, 0, full, offset, semanticDim);
                offset += semanticDim;
            }
            if (StructuralVector != null)
            {
                Array.Copy(StructuralVector, 0, full, offset, structuralDim);
                offset += structuralDim;
            }
            if (RelationVector != null)
            {
                Array.Copy(RelationVector, 0, full, offset, relationDim);
            }

            return full;
        }

        public int GetFullVectorDimension()
        {
            return (SemanticVector?.Length ?? 0)
                 + (StructuralVector?.Length ?? 0)
                 + (RelationVector?.Length ?? 0);
        }

        public double CosineSimilarity(FunctionVector other)
        {
            var v1 = GetFullVector();
            var v2 = other.GetFullVector();

            if (v1.Length != v2.Length || v1.Length == 0) return 0;

            double dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < v1.Length; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += v1[i] * v1[i];
                norm2 += v2[i] * v2[i];
            }

            double magnitude = Math.Sqrt(norm1) * Math.Sqrt(norm2);
            return magnitude > 0 ? dot / magnitude : 0;
        }

        public FunctionVector Clone()
        {
            return new FunctionVector
            {
                Id = Id,
                Name = Name,
                Signature = Signature,
                ClassName = ClassName,
                Namespace = Namespace,
                SemanticVector = SemanticVector?.ToArray(),
                StructuralVector = StructuralVector?.ToArray(),
                RelationVector = RelationVector?.ToArray(),
                ReturnType = ReturnType,
                Parameters = Parameters.Select(p => new ParameterInfo
                {
                    Name = p.Name,
                    Type = p.Type,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.DefaultValue
                }).ToList(),
                ComplexityScore = ComplexityScore,
                IsPure = IsPure,
                HasSideEffects = HasSideEffects,
                CalledBy = CalledBy.ToList(),
                Calls = Calls.ToList(),
                SourceCode = SourceCode,
                Source = Source,
                SourceWeight = SourceWeight
            };
        }

        public override string ToString()
        {
            return $"[{Source}] {Signature} (Score: {ComplexityScore})";
        }
    }
}
