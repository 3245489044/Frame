#region USINGS
using Agent.Transformers.Core;
#endregion

namespace Agent.AgentFlow.Interfaces
{
    public interface IDataPipeline
    {
        void Fit(Tensor X);

        Tensor Normalize(Tensor X);

        Tensor Denormalize(Tensor X);

        Tensor EncodeOneHot(string[] labels);

        int[] EncodeLabel(string[] labels);

        string[] Decode(int[] indices);
    }
}
