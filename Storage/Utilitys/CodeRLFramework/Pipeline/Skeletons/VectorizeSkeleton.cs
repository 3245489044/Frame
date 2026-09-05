
#region USINGS
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class VectorizeSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly VectorExtractor _extractor;

        public VectorizeSkeleton(VectorExtractor? extractor = null)
            : base("VectorizeSkeleton", "对函数引用图中的每个节点附加向量特征")
        {
            _extractor = extractor ?? new VectorExtractor();
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.FunctionSpace == null)
                throw new InvalidOperationException("FunctionSpace 未设置，请先执行 CodeExtractSkeleton");

            var space = input.FunctionSpace;

            foreach (var func in space.Functions)
            {
                if (func.SemanticVector == null || func.SemanticVector.Length == 0)
                {
                    func.SemanticVector = GenerateDefaultSemanticVector(func.Name);
                }

                if (func.StructuralVector == null || func.StructuralVector.Length == 0)
                {
                    func.StructuralVector = GenerateDefaultStructuralVector(func);
                }

                if (func.RelationVector == null || func.RelationVector.Length == 0)
                {
                    func.RelationVector = GenerateDefaultRelationVector(func);
                }
            }

            context.Items["VectorizeSkeleton.Info"] =
                $"向量化完成: 函数={space.Count}, 向量维度={space.VectorDimension}";

            return Task.FromResult(input);
        }

        private static double[] GenerateDefaultSemanticVector(string functionName)
        {
            var vector = new double[50];
            for (int i = 0; i < functionName.Length; i++)
            {
                int idx = Math.Abs(functionName[i].GetHashCode()) % 50;
                vector[idx] += 1.0 / functionName.Length;
            }
            return vector;
        }

        private static double[] GenerateDefaultStructuralVector(FunctionVector func)
        {
            var vector = new double[40];
            vector[30] = Math.Min(func.SourceCode.Length / 1000.0, 1.0);
            vector[31] = Math.Min(func.Parameters.Count / 10.0, 1.0);
            vector[32] = Math.Min(func.ComplexityScore / 100.0, 1.0);
            return vector;
        }

        private static double[] GenerateDefaultRelationVector(FunctionVector func)
        {
            var vector = new double[50];
            foreach (var callee in func.Calls)
            {
                int idx = Math.Abs(callee.GetHashCode()) % 25;
                vector[idx] += 1.0;
            }
            foreach (var caller in func.CalledBy)
            {
                int idx = 25 + (Math.Abs(caller.GetHashCode()) % 25);
                vector[idx] += 1.0;
            }
            return vector;
        }
    }
}
