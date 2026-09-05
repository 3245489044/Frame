
namespace TConfig
{
    public class TrPipeline
    {
        public string Name { get; set; } = string.Empty;

        public List<TrStep> Steps { get; set; } = new();

        public Dictionary<string, object>? FunctionMap { get; set; }

        public bool FlattenSingleResult { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class TrStep
    {
        public string Name { get; set; } = string.Empty;

        public Delegate? Function { get; set; }

        public object? Transformer { get; set; }

        public TrPipeline? SubPipeline { get; set; }

        public Func<object?, bool>? Condition { get; set; }

        public bool IsParallel { get; set; }

        public List<TrStep>? ParallelSteps { get; set; }


        public bool UseExplicitParameters { get; set; }

        public List<object?>? Parameters { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
