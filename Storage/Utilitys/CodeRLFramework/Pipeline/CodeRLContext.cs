
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using WorkFlowMethod.CentralPipeline;
using WorkFlowMethod.GraphFramework.Snapshots;
#endregion

namespace CodeRLFramework.Pipeline
{
    public class CodeRLContext
    {
        public WorkflowContext WorkflowCtx { get; }


        public string? CodebasePath { get; set; }

        public string? TemplateCode { get; set; }

        public string? TestCode { get; set; }

        public FunctionVectorSpace? FunctionSpace { get; set; }

        public GraphSnapshot? GraphSnapshot { get; set; }

        public CodeTokenizer? Tokenizer { get; set; }

        public int[]? GeneratedTokens { get; set; }

        public List<string>? DecodedFunctionNames { get; set; }

        public OutputVectorSpace? OutputSpace { get; set; }

        public string? GeneratedCode { get; set; }

        public double Reward { get; set; }

        public List<double> RewardHistory { get; } = new();

        public double BestReward { get; set; } = double.MinValue;

        public string? BestCode { get; set; }

        public int Episode { get; set; }

        public int MaxStepsPerEpisode { get; set; } = 50;

        public bool IsConverged { get; set; }

        public double ConvergenceThreshold { get; set; } = 0.01;

        public int ConvergenceWindow { get; set; } = 10;

        public WeightConfig? Weights { get; set; }

        public GraphNavigationContext? NavContext { get; set; }

        public Dictionary<string, object> Properties { get; set; } = new();

        public CodeRLContext(string? contextId = null)
        {
            WorkflowCtx = new WorkflowContext(contextId ?? $"CodeRL-{Guid.NewGuid():N}");
        }

        public CodeRLContext(WorkflowContext workflowCtx)
        {
            WorkflowCtx = workflowCtx ?? throw new ArgumentNullException(nameof(workflowCtx));
        }

        public void SetData(string key, object value) => WorkflowCtx.SetData(key, value);

        public T GetData<T>(string key) => WorkflowCtx.GetData<T>(key);

        public object? GetData(string key) => WorkflowCtx.GetData(key);

        public void Reset()
        {
            Episode = 0;
            RewardHistory.Clear();
            BestReward = double.MinValue;
            BestCode = null;
            IsConverged = false;
            GeneratedTokens = null;
            DecodedFunctionNames = null;
            OutputSpace = null;
            GeneratedCode = null;
            Reward = 0;
        }
    }
}
