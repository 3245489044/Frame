namespace CodeRLFramework.VectorSpace
{
    public class FunctionVectorSpace
    {
        public List<FunctionVector> Functions { get; set; } = new();

        public int VectorDimension { get; set; }

        public string Source { get; set; } = string.Empty;

        public double Weight { get; set; } = 1.0;


        public FunctionVectorSpace Fuse(
            FunctionVectorSpace other,
            double selfWeight,
            double otherWeight)
        {
            var result = new FunctionVectorSpace
            {
                VectorDimension = this.VectorDimension,
                Source = $"Fused({this.Source}:{selfWeight}, {other.Source}:{otherWeight})",
                Weight = 1.0
            };

            var funcIndex = this.Functions
                .GroupBy(f => f.Name)
                .ToDictionary(g => g.Key, g => g.First());

            var processedNames = new HashSet<string>();

            foreach (var func in other.Functions)
            {
                if (funcIndex.TryGetValue(func.Name, out var existing))
                {
                    var fused = FuseVectors(existing, func, selfWeight, otherWeight);
                    result.Functions.Add(fused);
                    processedNames.Add(func.Name);
                }
            }

            foreach (var func in this.Functions)
            {
                if (!processedNames.Contains(func.Name))
                {
                    var scaled = ScaleVector(func, selfWeight);
                    result.Functions.Add(scaled);
                }
            }

            foreach (var func in other.Functions)
            {
                if (!processedNames.Contains(func.Name))
                {
                    var scaled = ScaleVector(func, otherWeight);
                    result.Functions.Add(scaled);
                }
            }

            return result;
        }

        private static FunctionVector FuseVectors(
            FunctionVector a, FunctionVector b,
            double weightA, double weightB)
        {
            var result = new FunctionVector
            {
                Id = a.Id,
                Name = a.Name,
                Signature = a.Signature,
                ClassName = a.ClassName,
                Namespace = a.Namespace,
                ReturnType = a.ReturnType,
                Parameters = a.Parameters,
                ComplexityScore = (int)(a.ComplexityScore * weightA + b.ComplexityScore * weightB),
                IsPure = a.IsPure && b.IsPure,
                HasSideEffects = a.HasSideEffects || b.HasSideEffects,
                SourceCode = weightA >= weightB ? a.SourceCode : b.SourceCode,
                Source = "fused",
                SourceWeight = 1.0
            };

            result.SemanticVector = WeightedAverage(a.SemanticVector, b.SemanticVector, weightA, weightB);
            result.StructuralVector = WeightedAverage(a.StructuralVector, b.StructuralVector, weightA, weightB);
            result.RelationVector = WeightedAverage(a.RelationVector, b.RelationVector, weightA, weightB);

            result.Calls.AddRange(a.Calls);
            result.Calls.AddRange(b.Calls.Where(c => !result.Calls.Contains(c)));
            result.CalledBy.AddRange(a.CalledBy);
            result.CalledBy.AddRange(b.CalledBy.Where(c => !result.CalledBy.Contains(c)));

            return result;
        }

        private static double[]? WeightedAverage(double[]? v1, double[]? v2, double w1, double w2)
        {
            if (v1 == null && v2 == null) return null;
            if (v1 == null) return v2!.Select(x => x * w2).ToArray();
            if (v2 == null) return v1.Select(x => x * w1).ToArray();

            int len = Math.Max(v1.Length, v2.Length);
            var result = new double[len];

            for (int i = 0; i < len; i++)
            {
                double val1 = i < v1.Length ? v1[i] : 0;
                double val2 = i < v2.Length ? v2[i] : 0;
                result[i] = val1 * w1 + val2 * w2;
            }

            return result;
        }

        private static FunctionVector ScaleVector(FunctionVector v, double scale)
        {
            var result = new FunctionVector
            {
                Id = v.Id,
                Name = v.Name,
                Signature = v.Signature,
                ClassName = v.ClassName,
                Namespace = v.Namespace,
                ReturnType = v.ReturnType,
                Parameters = v.Parameters,
                ComplexityScore = (int)(v.ComplexityScore * scale),
                IsPure = v.IsPure,
                HasSideEffects = v.HasSideEffects,
                SourceCode = v.SourceCode,
                Source = v.Source,
                SourceWeight = v.SourceWeight * scale,
                SemanticVector = v.SemanticVector?.Select(x => x * scale).ToArray(),
                StructuralVector = v.StructuralVector?.Select(x => x * scale).ToArray(),
                RelationVector = v.RelationVector?.Select(x => x * scale).ToArray(),
                Calls = v.Calls.ToList(),
                CalledBy = v.CalledBy.ToList()
            };

            return result;
        }


        public List<FunctionVector> SearchByName(string name)
        {
            return Functions
                .Where(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.SourceWeight)
                .ToList();
        }

        public List<(FunctionVector func, double similarity)> SearchByVector(
            double[] queryVector, int topK = 10)
        {
            return Functions
                .Select(f => (func: f, similarity: CosineSimilarity(queryVector, f.GetFullVector())))
                .OrderByDescending(x => x.similarity)
                .Take(topK)
                .ToList();
        }

        public List<FunctionVector> FilterByReturnType(string returnType)
        {
            return Functions
                .Where(f => f.ReturnType == returnType)
                .ToList();
        }

        public List<FunctionVector> GetCompatibleFunctions(
            string returnType, List<string> argTypes)
        {
            return Functions
                .Where(f => f.ReturnType == returnType)
                .Where(f => f.Parameters.Count == argTypes.Count)
                .Where(f => f.Parameters.Zip(argTypes, (p, a) => p.Type == a).All(x => x))
                .ToList();
        }

        public int Count => Functions.Count;

        public void AddFunction(FunctionVector func)
        {
            Functions.Add(func);
        }

        public void Clear()
        {
            Functions.Clear();
        }

        private static double CosineSimilarity(double[] v1, double[] v2)
        {
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

        public override string ToString()
        {
            return $"FunctionVectorSpace [{Source}] - {Functions.Count} functions, {VectorDimension} dims";
        }
    }
}
