
namespace Share.TreeTraceFramework.Models
{
    public class TraceQueryStep
    {
        public TraceQueryStepType Type { get; }

        public string? StringValue { get; }

        public bool? BoolFlag { get; }

        public IReadOnlyList<object?>? CollectionValue { get; }

        private TraceQueryStep(TraceQueryStepType type, string? stringValue = null, bool? boolFlag = null, IEnumerable<object?>? collectionValue = null)
        {
            Type = type;
            StringValue = stringValue;
            BoolFlag = boolFlag;
            CollectionValue = collectionValue?.ToList().AsReadOnly();
        }


        public static TraceQueryStep WhereHasChildren() => new(TraceQueryStepType.WhereHasChildren);
        public static TraceQueryStep WhereHasInputs() => new(TraceQueryStepType.WhereHasInputs);
        public static TraceQueryStep WhereHasOutputs() => new(TraceQueryStepType.WhereHasOutputs);
        public static TraceQueryStep WhereNoOutputs() => new(TraceQueryStepType.WhereNoOutputs);
        public static TraceQueryStep WhereName(string name, bool exact = true)
            => new(TraceQueryStepType.WhereName, name, exact);
        public static TraceQueryStep WhereLabel(string label)
            => new(TraceQueryStepType.WhereLabel, label);
        public static TraceQueryStep WhereInputsMatch(IEnumerable<object?> inputs)
            => new(TraceQueryStepType.WhereInputsMatch, collectionValue: inputs);
        public static TraceQueryStep WhereOutputsMatch(IEnumerable<object?> outputs)
            => new(TraceQueryStepType.WhereOutputsMatch, collectionValue: outputs);
        public static TraceQueryStep SelectChildren() => new(TraceQueryStepType.SelectChildren);
        public static TraceQueryStep SelectGrandChildren() => new(TraceQueryStepType.SelectGrandChildren);
        public static TraceQueryStep SelectInputs() => new(TraceQueryStepType.SelectInputs);
        public static TraceQueryStep SelectOutputs() => new(TraceQueryStepType.SelectOutputs);
        public static TraceQueryStep First() => new(TraceQueryStepType.First);
        public static TraceQueryStep Take(int count) => new(TraceQueryStepType.Take, count.ToString());
        public static TraceQueryStep Skip(int count) => new(TraceQueryStepType.Skip, count.ToString());
    }
}
