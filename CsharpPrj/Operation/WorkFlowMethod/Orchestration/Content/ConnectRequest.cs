
using Share.LogicComposition.Models;

namespace WorkFlowMethod.Orchestration.Content
{
    public class ConnectRequest
    {
        public string? SourceEndNodeCallkey { get; set; }

        public string? TargetStartNodeCallkey { get; set; }

        public CompositionOptions? MergeOptions { get; set; }
    }
}
