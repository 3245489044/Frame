
#region USINGS
using CodeRLFramework.Pipeline.Placeholder;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class PlaceholderResolveSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly PlaceholderResolver _resolver;

        public PlaceholderResolveSkeleton(PlaceholderResolver? resolver = null)
            : base("PlaceholderResolveSkeleton", "解析训练管线中的 Placeholder 引用语法")
        {
            _resolver = resolver ?? new PlaceholderResolver("CodeRLPlaceholderResolver");
        }

        public PlaceholderResolver Resolver => _resolver;

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var resolveContext = BuildResolveContext(input);

            if (!string.IsNullOrEmpty(input.TemplateCode) &&
                PlaceholderDefinition.ContainsPlaceholder(input.TemplateCode))
            {
                var templateResult = _resolver.Resolve(input.TemplateCode, resolveContext);
                input.TemplateCode = templateResult.ResolvedText;

                context.Items["PlaceholderResolveSkeleton.TemplatePlaceholders"] =
                    templateResult.Placeholders.Count;
                context.Items["PlaceholderResolveSkeleton.TemplateResolved"] =
                    templateResult.IsAllResolved;

                if (templateResult.Placeholders.Count > 0)
                {
                    var details = string.Join("; ",
                        templateResult.Placeholders.Select(p => p.ToString()));
                    context.Items["PlaceholderResolveSkeleton.TemplateDetails"] = details;
                }
            }

            if (!string.IsNullOrEmpty(input.GeneratedCode) &&
                PlaceholderDefinition.ContainsPlaceholder(input.GeneratedCode))
            {
                var codeResult = _resolver.Resolve(input.GeneratedCode, resolveContext);
                input.GeneratedCode = codeResult.ResolvedText;

                context.Items["PlaceholderResolveSkeleton.CodePlaceholders"] =
                    codeResult.Placeholders.Count;
                context.Items["PlaceholderResolveSkeleton.CodeResolved"] =
                    codeResult.IsAllResolved;

                if (codeResult.Placeholders.Count > 0)
                {
                    var details = string.Join("; ",
                        codeResult.Placeholders.Select(p => p.ToString()));
                    context.Items["PlaceholderResolveSkeleton.CodeDetails"] = details;
                }
            }

            if (input.Properties.TryGetValue("NavigationPrompt", out var promptObj) &&
                promptObj is string prompt &&
                PlaceholderDefinition.ContainsPlaceholder(prompt))
            {
                var promptResult = _resolver.Resolve(prompt, resolveContext);
                input.Properties["NavigationPrompt"] = promptResult.ResolvedText;

                context.Items["PlaceholderResolveSkeleton.PromptPlaceholders"] =
                    promptResult.Placeholders.Count;
            }

            var stats = _resolver.GetStats();
            context.Items["PlaceholderResolveSkeleton.Stats"] = stats.ToString();

            return Task.FromResult(input);
        }

        private PlaceholderResolveContext BuildResolveContext(CodeRLContext input)
        {
            var resolveContext = new PlaceholderResolveContext
            {
                InputCode = input.TemplateCode,
                PreviousCode = input.BestCode,
                GraphNodeNames = ExtractGraphNodeNames(input),
                NavigationPath = ExtractNavigationPath(input),
                CurrentStepIndex = input.Episode
            };

            resolveContext.CustomResolver = rawText =>
            {
                if (rawText.StartsWith("@ctx:", StringComparison.OrdinalIgnoreCase))
                {
                    var key = rawText[5..];
                    return input.WorkflowCtx.ContainsKey(key)
                        ? input.WorkflowCtx.GetData(key)?.ToString()
                        : null;
                }
                return null;
            };

            return resolveContext;
        }

        private static HashSet<string> ExtractGraphNodeNames(CodeRLContext input)
        {
            var names = new HashSet<string>();

            if (input.GraphSnapshot != null)
            {
                foreach (var node in input.GraphSnapshot.Nodes)
                {
                    if (!string.IsNullOrEmpty(node.Name))
                        names.Add(node.Name);
                }
            }

            if (input.FunctionSpace != null)
            {
                foreach (var func in input.FunctionSpace.Functions)
                {
                    if (!string.IsNullOrEmpty(func.Name))
                        names.Add(func.Name);
                }
            }

            return names;
        }

        private static List<string> ExtractNavigationPath(CodeRLContext input)
        {
            if (input.NavContext != null)
                return new List<string>(input.NavContext.VisitedPath);

            if (input.Properties.TryGetValue("GraphNavigationContext", out var navCtxObj) &&
                navCtxObj is Models.GraphNavigationContext navCtx)
            {
                return new List<string>(navCtx.VisitedPath);
            }

            if (input.DecodedFunctionNames != null)
                return new List<string>(input.DecodedFunctionNames);

            return new List<string>();
        }
    }
}
