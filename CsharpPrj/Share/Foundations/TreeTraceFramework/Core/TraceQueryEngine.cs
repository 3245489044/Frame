
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Core
{
    public class TraceQueryEngine : ITraceQueryEngine<TraceNode>
    {
        public IEnumerable<TraceNode> Execute(ITraceTree<TraceNode> tree, TraceQuery<TraceNode> query)
        {
            if (tree?.Root == null || query == null || query.Steps.Count == 0)
                return Enumerable.Empty<TraceNode>();

            return Execute(new[] { tree.Root }, query);
        }

        public IEnumerable<TraceNode> Execute(IEnumerable<TraceNode> nodes, TraceQuery<TraceNode> query)
        {
            if (nodes == null || query == null || query.Steps.Count == 0)
                return Enumerable.Empty<TraceNode>();

            return ExecuteInternal(nodes, query);
        }

        private static IEnumerable<TraceNode> ExecuteInternal(
            IEnumerable<TraceNode> source, TraceQuery<TraceNode> query)
        {
            IEnumerable<TraceNode> current = source;

            foreach (var step in query.Steps)
            {
                switch (step.Type)
                {
                    case TraceQueryStepType.WhereHasChildren:
                        current = current.Where(n => n.Children.Any());
                        break;

                    case TraceQueryStepType.WhereHasInputs:
                        current = current.Where(n => n.Inputs.Any());
                        break;

                    case TraceQueryStepType.WhereHasOutputs:
                        current = current.Where(n => n.Outputs.Any());
                        break;

                    case TraceQueryStepType.WhereNoOutputs:
                        current = current.Where(n => !n.Outputs.Any());
                        break;

                    case TraceQueryStepType.WhereName:
                        current = step.BoolFlag == true
                            ? current.Where(n => n.Name == step.StringValue)
                            : current.Where(n => n.Name != null &&
                                n.Name.Contains(step.StringValue ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                        break;

                    case TraceQueryStepType.WhereLabel:
                        current = current.Where(n => n.Labels.Contains(step.StringValue ?? string.Empty));
                        break;

                    case TraceQueryStepType.WhereInputsMatch:
                        current = current.Where(n => CollectionsMatch(n.Inputs, step.CollectionValue));
                        break;

                    case TraceQueryStepType.WhereOutputsMatch:
                        current = current.Where(n => CollectionsMatch(n.Outputs, step.CollectionValue));
                        break;

                    case TraceQueryStepType.SelectChildren:
                        current = current.SelectMany(n => n.Children);
                        break;

                    case TraceQueryStepType.SelectGrandChildren:
                        current = current.SelectMany(n => n.Children).SelectMany(n => n.Children);
                        break;

                    case TraceQueryStepType.SelectInputs:
                        current = current.SelectMany(n => n.Inputs).OfType<TraceNode>();
                        break;

                    case TraceQueryStepType.SelectOutputs:
                        current = current.SelectMany(n => n.Outputs).OfType<TraceNode>();
                        break;

                    case TraceQueryStepType.First:
                        current = current.Take(1);
                        break;

                    case TraceQueryStepType.Take:
                        current = int.TryParse(step.StringValue, out var takeCount) ? current.Take(takeCount) : current;
                        break;

                    case TraceQueryStepType.Skip:
                        current = int.TryParse(step.StringValue, out var skipCount) ? current.Skip(skipCount) : current;
                        break;

                    default:
                        break;
                }
            }

            return current;
        }

        private static bool CollectionsMatch(IReadOnlyList<object?> nodeValues, IEnumerable<object?>? targetValues)
        {
            var target = targetValues?
                .Select(i => i?.ToString())
                .OrderBy(i => i)
                .ToList();

            if (target == null) return false;

            var source = nodeValues
                .Select(i => i?.ToString())
                .OrderBy(i => i)
                .ToList();

            return source.SequenceEqual(target);
        }
    }
}
