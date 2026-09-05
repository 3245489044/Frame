#region USINGS
using Agent.Transformers.Core;
#endregion

namespace Agent.AgentFlow.Interfaces
{
    public interface IOutputPipeline
    {
        Tensor Denormalize(Tensor output);

        Tensor GetProbabilities(Tensor logits);

        int[] GetPredictions(Tensor logits);

        int SampleToken(Tensor logits, double temperature = 1.0);

        string GetSummary();
    }
}
