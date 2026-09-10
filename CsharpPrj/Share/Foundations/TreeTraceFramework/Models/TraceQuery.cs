
using Share.TreeTraceFramework.Abstractions;

namespace Share.TreeTraceFramework.Models
{
    public class TraceQuery<TNode>
        where TNode : ITraceNode<TNode>
    {
        public IReadOnlyList<TraceQueryStep> Steps => _steps.AsReadOnly();
        private readonly List<TraceQueryStep> _steps = new();

        public TraceQuery<TNode> AddStep(TraceQueryStep step)
        {
            _steps.Add(step);
            return this;
        }


        public TraceQuery<TNode> WhereHasChildren() =>
            AddStep(TraceQueryStep.WhereHasChildren());

        public TraceQuery<TNode> WhereHasInputs() =>
            AddStep(TraceQueryStep.WhereHasInputs());

        public TraceQuery<TNode> WhereHasOutputs() =>
            AddStep(TraceQueryStep.WhereHasOutputs());

        public TraceQuery<TNode> WhereNoOutputs() =>
            AddStep(TraceQueryStep.WhereNoOutputs());

        public TraceQuery<TNode> WhereName(string name) =>
            AddStep(TraceQueryStep.WhereName(name, exact: true));

        public TraceQuery<TNode> WhereNameContains(string pattern) =>
            AddStep(TraceQueryStep.WhereName(pattern, exact: false));

        public TraceQuery<TNode> WhereLabel(string label) =>
            AddStep(TraceQueryStep.WhereLabel(label));

        public TraceQuery<TNode> WhereInputsMatch(IEnumerable<object?> inputs) =>
            AddStep(TraceQueryStep.WhereInputsMatch(inputs));

        public TraceQuery<TNode> WhereOutputsMatch(IEnumerable<object?> outputs) =>
            AddStep(TraceQueryStep.WhereOutputsMatch(outputs));


        public TraceQuery<TNode> SelectChildren() =>
            AddStep(TraceQueryStep.SelectChildren());

        public TraceQuery<TNode> SelectGrandChildren() =>
            AddStep(TraceQueryStep.SelectGrandChildren());

        public TraceQuery<TNode> SelectInputs() =>
            AddStep(TraceQueryStep.SelectInputs());

        public TraceQuery<TNode> SelectOutputs() =>
            AddStep(TraceQueryStep.SelectOutputs());


        public TraceQuery<TNode> First() =>
            AddStep(TraceQueryStep.First());

        public TraceQuery<TNode> Take(int count) =>
            AddStep(TraceQueryStep.Take(count));

        public TraceQuery<TNode> Skip(int count) =>
            AddStep(TraceQueryStep.Skip(count));

        public static TraceQuery<TNode> FindNodeOutputs() =>
            new TraceQuery<TNode>()
                .WhereHasChildren()
                .SelectChildren()
                .WhereHasOutputs()
                .SelectOutputs();

        public static TraceQuery<TNode> FindNodeOutputsMatchName(string namePattern) =>
            new TraceQuery<TNode>()
                .WhereHasChildren()
                .SelectChildren()
                .WhereNameContains(namePattern)
                .WhereHasOutputs()
                .SelectOutputs();

        public static TraceQuery<TNode> FindNodeByInputsSet(IEnumerable<object?> inputs) =>
            new TraceQuery<TNode>()
                .WhereInputsMatch(inputs);

        public TraceQuery<TNode> Clone()
        {
            var clone = new TraceQuery<TNode>();
            clone._steps.AddRange(_steps);
            return clone;
        }
    }
}
