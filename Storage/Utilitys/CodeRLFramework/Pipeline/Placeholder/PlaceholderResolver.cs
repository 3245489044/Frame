
#region USINGS
using System.Diagnostics;
#endregion

namespace CodeRLFramework.Pipeline.Placeholder
{
    public class PlaceholderResolver
    {
        public string Name { get; }

        public int MaxRecursionDepth { get; set; } = 5;

        public bool KeepUnresolvedAsRaw { get; set; } = true;

        public List<PlaceholderResolveResult> ResolveHistory { get; } = new();

        public PlaceholderResolver(string name = "DefaultPlaceholderResolver")
        {
            Name = name;
        }

        public PlaceholderResolveResult Resolve(string text, PlaceholderResolveContext context)
        {
            var sw = Stopwatch.StartNew();
            var result = new PlaceholderResolveResult
            {
                ResolvedText = text,
                Placeholders = PlaceholderDefinition.ParseAll(text)
            };

            if (result.Placeholders.Count == 0)
            {
                result.ResolvedText = text;
                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
                ResolveHistory.Add(result);
                return result;
            }

            result.ResolvedText = ResolveRecursive(text, context, 0);

            foreach (var ph in result.Placeholders)
            {
                ph.IsResolved = !result.ResolvedText.Contains(ph.RawText);
                ph.ResolvedAt = DateTime.UtcNow;
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            ResolveHistory.Add(result);

            return result;
        }

        private string ResolveRecursive(string text, PlaceholderResolveContext context, int depth)
        {
            if (depth >= MaxRecursionDepth)
                return text;

            if (!PlaceholderDefinition.ContainsPlaceholder(text))
                return text;

            var placeholders = PlaceholderDefinition.ParseAll(text);
            string resolved = text;

            foreach (var ph in placeholders)
            {
                string? value = ResolveSingle(ph, context);

                if (value != null)
                {
                    resolved = resolved.Replace(ph.RawText, value);
                    ph.ResolvedValue = value;
                }
                else if (!KeepUnresolvedAsRaw)
                {
                    resolved = resolved.Replace(ph.RawText, string.Empty);
                    ph.ErrorMessage = $"无法解析 Placeholder: {ph.RawText}";
                }
            }

            if (PlaceholderDefinition.ContainsPlaceholder(resolved))
            {
                resolved = ResolveRecursive(resolved, context, depth + 1);
            }

            return resolved;
        }

        private string? ResolveSingle(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            return ph.Type switch
            {
                PlaceholderType.Input => ResolveInput(ph, context),
                PlaceholderType.Prev => ResolvePrev(ph, context),
                PlaceholderType.Node => ResolveNode(ph, context),
                PlaceholderType.PrevStep => ResolvePrevStep(ph, context),
                PlaceholderType.Context => ResolveContext(ph, context),
                PlaceholderType.Custom => ResolveCustom(ph, context),
                _ => null
            };
        }

        private string? ResolveInput(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            if (string.IsNullOrEmpty(ph.Name))
                return context.InputCode;

            return ph.Name.ToLower() switch
            {
                "template" => context.InputCode,
                "test" => null,
                _ => context.InputCode
            };
        }

        private string? ResolvePrev(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            return context.PreviousCode;
        }

        private string? ResolveNode(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            if (!string.IsNullOrEmpty(ph.Name))
            {
                if (context.GraphNodeNames.Contains(ph.Name))
                    return ph.Name;
                return null;
            }

            return context.NavigationPath.Count > 0
                ? context.NavigationPath[^1]
                : null;
        }

        private string? ResolvePrevStep(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            return context.GetPrevStepNode();
        }

        private string? ResolveContext(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            if (string.IsNullOrEmpty(ph.Name))
            {
                var path = context.NavigationPath.Count > 0
                    ? string.Join(" → ", context.NavigationPath)
                    : "(空路径)";
                return $"[导航路径: {path}]";
            }

            return ph.Name.ToLower() switch
            {
                "path" => string.Join(" → ", context.NavigationPath),
                "count" => context.NavigationPath.Count.ToString(),
                "current" => context.NavigationPath.Count > 0
                    ? context.NavigationPath[^1]
                    : null,
                _ => null
            };
        }

        private string? ResolveCustom(PlaceholderDefinition ph, PlaceholderResolveContext context)
        {
            return context.CustomResolver?.Invoke(ph.RawText);
        }

        public void ClearHistory()
        {
            ResolveHistory.Clear();
        }

        public PlaceholderResolveStats GetStats()
        {
            var stats = new PlaceholderResolveStats
            {
                TotalResolves = ResolveHistory.Count,
                TotalPlaceholders = ResolveHistory.Sum(r => r.Placeholders.Count),
                ResolvedPlaceholders = ResolveHistory.Sum(r => r.Placeholders.Count(p => p.IsResolved)),
                UnresolvedPlaceholders = ResolveHistory.Sum(r => r.Placeholders.Count(p => !p.IsResolved)),
                TotalElapsedMs = ResolveHistory.Sum(r => r.ElapsedMs)
            };
            return stats;
        }
    }

    public class PlaceholderResolveStats
    {
        public int TotalResolves { get; set; }

        public int TotalPlaceholders { get; set; }

        public int ResolvedPlaceholders { get; set; }

        public int UnresolvedPlaceholders { get; set; }

        public long TotalElapsedMs { get; set; }

        public double SuccessRate =>
            TotalPlaceholders > 0
                ? (double)ResolvedPlaceholders / TotalPlaceholders
                : 1.0;

        public override string ToString()
        {
            return $"Placeholder 解析统计: {ResolvedPlaceholders}/{TotalPlaceholders} 已解析 " +
                   $"({SuccessRate:P1}), 总耗时 {TotalElapsedMs}ms, " +
                   $"共 {TotalResolves} 次解析";
        }
    }
}
