
#region USINGS
using CodeRLFramework.Generation;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class TokenizeSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        public TokenizeSkeleton()
            : base("TokenizeSkeleton", "将函数向量空间编码为 Token 序列")
        {
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.FunctionSpace == null)
                throw new InvalidOperationException("FunctionSpace 未设置");

            if (input.Tokenizer == null)
            {
                input.Tokenizer = new CodeTokenizer(input.FunctionSpace);
            }

            var functionNames = ExtractFunctionNames(input.TemplateCode ?? "");

            input.GeneratedTokens = input.Tokenizer.Encode(functionNames);

            context.Items["TokenizeSkeleton.Info"] =
                $"Tokenize 完成: 函数名={functionNames.Count}, Token序列长度={input.GeneratedTokens.Length}, " +
                $"词汇表大小={input.Tokenizer.VocabSize}";

            return Task.FromResult(input);
        }

        private static List<string> ExtractFunctionNames(string templateCode)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(templateCode))
                return names;

            int i = 0;
            while (i < templateCode.Length)
            {
                if (char.IsWhiteSpace(templateCode[i]) ||
                    templateCode[i] == ';' || templateCode[i] == '{' ||
                    templateCode[i] == '}' || templateCode[i] == '(' ||
                    templateCode[i] == ')')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(templateCode[i]) || templateCode[i] == '_')
                {
                    int start = i;
                    while (i < templateCode.Length &&
                           (char.IsLetterOrDigit(templateCode[i]) || templateCode[i] == '_'))
                        i++;

                    string word = templateCode[start..i];
                    if (!IsCSharpKeyword(word))
                    {
                        names.Add(word);
                    }
                }
                else
                {
                    i++;
                }
            }

            return names;
        }

        private static bool IsCSharpKeyword(string word)
        {
            return word switch
            {
                "using" or "namespace" or "class" or "struct" or "interface" or
                "public" or "private" or "protected" or "internal" or "static" or
                "void" or "int" or "string" or "bool" or "double" or "float" or
                "var" or "new" or "return" or "if" or "else" or "for" or "foreach" or
                "while" or "do" or "switch" or "case" or "break" or "continue" or
                "try" or "catch" or "finally" or "throw" or "async" or "await" or
                "this" or "base" or "null" or "true" or "false" or "in" or "out" or
                "ref" or "readonly" or "const" or "override" or "virtual" or
                "abstract" or "sealed" or "partial" or "record" or "init" or
                "get" or "set" or "value" or "where" or "select" or "from" => true,
                _ => false
            };
        }
    }
}
