
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Output;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class ConvertSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly VectorToCodeConverter _converter;

        public ConvertSkeleton(VectorToCodeConverter converter)
            : base("ConvertSkeleton", "将生成的 Token 序列解码并转换为代码")
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Tokenizer == null)
                throw new InvalidOperationException("Tokenizer 未设置");
            if (input.GeneratedTokens == null)
                throw new InvalidOperationException("GeneratedTokens 未设置");

            input.DecodedFunctionNames = input.Tokenizer.DecodeToNames(input.GeneratedTokens);

            input.OutputSpace = BuildOutputSpace(input);

            input.GeneratedCode = _converter.Convert(input.OutputSpace, input.TemplateCode ?? "");

            context.Items["ConvertSkeleton.Info"] =
                $"转换完成: 函数名={input.DecodedFunctionNames.Count}, " +
                $"代码长度={input.GeneratedCode?.Length ?? 0}";

            return Task.FromResult(input);
        }

        private static OutputVectorSpace BuildOutputSpace(CodeRLContext input)
        {
            var vectors = new List<OutputVector>();
            var space = input.FunctionSpace;

            if (space == null || input.DecodedFunctionNames == null)
                return new OutputVectorSpace();

            foreach (var name in input.DecodedFunctionNames)
            {
                var matches = space.SearchByName(name);
                if (matches.Count > 0)
                {
                    var func = matches[0];
                    vectors.Add(new OutputVector
                    {
                        FunctionId = func.Id,
                        FunctionName = func.Name,
                        Vector = func.GetFullVector(),
                        Probability = 1.0
                    });
                }
            }

            return new OutputVectorSpace
            {
                Vectors = vectors,
                Confidence = vectors.Count > 0
                    ? vectors.Average(v => v.Probability)
                    : 0,
                Temperature = input.Weights?.Temperature ?? 1.0
            };
        }
    }
}
