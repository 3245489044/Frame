namespace WorkFlowMethod.LoopFramework.Core
{
    public class ActionResult
    {
        public bool Success { get; set; }

        public Dictionary<string, object> Outputs { get; set; } = new();

        public TimeSpan Duration { get; set; }

        public string Error { get; set; }
    }
}
