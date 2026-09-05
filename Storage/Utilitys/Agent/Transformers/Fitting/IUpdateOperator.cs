#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public interface IUpdateOperator
    {
        string Name { get; }

        void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate);
    }
}
