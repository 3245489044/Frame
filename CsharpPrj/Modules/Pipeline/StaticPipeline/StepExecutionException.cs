
#nullable enable
using System;

namespace Modules.Pipeline.StaticPipeline
{
    public class StepExecutionException : Exception
    {
        public string StepName { get; }

        public StepExecutionException(string stepName, Exception inner)
            : base($"步骤 '{stepName}' 执行失败: {inner.Message}", inner)
        {
            StepName = stepName ?? throw new ArgumentNullException(nameof(stepName));
        }
    }
}


