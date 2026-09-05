
#region USINGS
using CodeRLFramework.VectorSpace;
using GraphFoundation.Core;
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Core;
using WfmFunctionVector = CodeRLFramework.VectorSpace.FunctionVector;
using WorkFlowMethod.GraphFramework.Snapshots;
using NodeConfig = WorkFlowMethod.Orchestration.Config.NodeConfig;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class CodeExtractSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly VectorExtractor _extractor;

        public CodeExtractSkeleton(VectorExtractor? extractor = null)
            : base("CodeExtractSkeleton", "从代码库提取函数引用图，构建 CoreGraph 结构")
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

            var codebasePath = input.CodebasePath;
            if (string.IsNullOrEmpty(codebasePath))
                throw new InvalidOperationException("CodebasePath 未设置");

            var codeFiles = Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories);
            var allCode = new List<string>();
            foreach (var file in codeFiles)
            {
                try
                {
                    allCode.Add(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    context.Items[$"CodeExtractSkeleton.FileError.{file}"] = ex.Message;
                }
            }

            FunctionVectorSpace functionSpace;
            if (allCode.Count == 0 && input.FunctionSpace != null && input.FunctionSpace.Functions.Count > 0)
            {
                functionSpace = input.FunctionSpace;
                context.Items["CodeExtractSkeleton.Info"] =
                    $"代码提取跳过: 无代码文件，使用已有函数空间 (函数={functionSpace.Count})";
            }
            else
            {
                functionSpace = _extractor.ExtractFromMultiple(allCode, "codebase");
            }

            var graph = new CoreGraph<string, FunctionVector, object>();

            foreach (var func in functionSpace.Functions)
            {
                graph.AddNode(func.Name, func);
            }

            foreach (var func in functionSpace.Functions)
            {
                foreach (var callee in func.Calls)
                {
                    if (graph.ContainsNode(callee))
                    {
                        graph.AddEdge(func.Name, callee, new object());
                    }
                }
            }

            input.GraphSnapshot = GraphSnapshot.CreateFrom(
                graph,
                graphId: $"CodeRL_{Path.GetFileName(codebasePath)}",
                version: 1);

            input.FunctionSpace = functionSpace;

            context.Items["CodeExtractSkeleton.Info"] =
                $"代码提取完成: 文件={codeFiles.Length}, 函数={functionSpace.Count}, 节点={graph.NodeCount}, 边={graph.EdgeCount}";

            return Task.FromResult(input);
        }
    }
}
