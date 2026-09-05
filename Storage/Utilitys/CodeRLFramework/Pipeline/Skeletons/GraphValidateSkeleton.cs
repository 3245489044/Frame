
#region USINGS
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Snapshots;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class GraphValidateSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        public GraphValidateSkeleton()
            : base("GraphValidateSkeleton", "验证函数引用图的拓扑完整性")
        {
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var snapshot = input.GraphSnapshot;
            if (snapshot == null)
                throw new InvalidOperationException("GraphSnapshot 未设置，请先执行 CodeExtractSkeleton");

            bool isValid = snapshot.Validate(out var errors);

            if (input.FunctionSpace != null)
            {
                var spaceFuncNames = new HashSet<string>(
                    input.FunctionSpace.Functions.Select(f => f.Name));

                var missingInSpace = snapshot.NodeKeys
                    .Where(k => !spaceFuncNames.Contains(k))
                    .ToList();

                if (missingInSpace.Count > 0)
                {
                    context.Items["GraphValidateSkeleton.MissingInSpace"] =
                        string.Join(", ", missingInSpace);
                }

                var missingInGraph = spaceFuncNames
                    .Where(n => !snapshot.NodeKeys.Contains(n))
                    .ToList();

                if (missingInGraph.Count > 0)
                {
                    context.Items["GraphValidateSkeleton.MissingInGraph"] =
                        string.Join(", ", missingInGraph);
                }
            }

            context.Items["GraphValidateSkeleton.IsValid"] = isValid;
            context.Items["GraphValidateSkeleton.NodeCount"] = snapshot.NodeCount;
            context.Items["GraphValidateSkeleton.EdgeCount"] = snapshot.EdgeCount;

            if (!isValid)
            {
                context.Items["GraphValidateSkeleton.Errors"] =
                    string.Join("; ", errors);
            }

            context.Items["GraphValidateSkeleton.Info"] =
                $"图验证完成: 有效={isValid}, 节点={snapshot.NodeCount}, 边={snapshot.EdgeCount}" +
                (errors.Count > 0 ? $", 错误={errors.Count}" : "");

            return Task.FromResult(input);
        }
    }
}
