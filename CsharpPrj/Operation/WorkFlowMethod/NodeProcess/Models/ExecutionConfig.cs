
using System.Collections.Generic;
using WorkflowExecution.Execution;

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{
    public class ExecuteConfig
    {
        public List<string> ParameterKeys { get; set; }
        public List<string> ResultKeys { get; set; }
        public FuncPipelineParameters FuncPipeline { get; set; } = new();
    }

}
