
#region USINGS
using WorkFlowMethod.GraphFramework.Snapshots;
#endregion

namespace CodeRLFramework.Pipeline.Models
{
    public class NavigationStep
    {
        public int StepIndex { get; set; }

        public string CurrentNode { get; set; } = string.Empty;

        public string SelectedNode { get; set; } = string.Empty;

        public List<string> Candidates { get; set; } = new();

        public double Confidence { get; set; }

        public bool IsFallback { get; set; }

        public double RewardContribution { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class GraphNavigationContext
    {
        public string ContextId { get; }

        public string? GraphSnapshotId { get; set; }

        public string? StartNode { get; set; }

        public string? CurrentNode { get; set; }

        public List<string> VisitedPath { get; } = new();

        public HashSet<string> VisitedSet { get; } = new();

        public List<NavigationStep> Steps { get; } = new();

        public int MaxSteps { get; set; } = 50;

        public int CurrentStep => Steps.Count;

        public bool IsCompleted { get; set; }

        public bool IsDeadEnd { get; set; }

        public double ConfidenceThreshold { get; set; } = 0.3;

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public GraphNavigationContext(string? contextId = null)
        {
            ContextId = contextId ?? $"NavCtx-{Guid.NewGuid():N}";
        }

        public NavigationStep RecordStep(
            string currentNode,
            string selectedNode,
            List<string> candidates,
            double confidence,
            bool isFallback = false)
        {
            var step = new NavigationStep
            {
                StepIndex = CurrentStep,
                CurrentNode = currentNode,
                SelectedNode = selectedNode,
                Candidates = candidates,
                Confidence = confidence,
                IsFallback = isFallback,
                Timestamp = DateTime.UtcNow
            };

            Steps.Add(step);
            VisitedPath.Add(selectedNode);
            VisitedSet.Add(selectedNode);
            CurrentNode = selectedNode;

            return step;
        }

        public List<string> GetUnvisitedCandidates(List<string> allCandidates)
        {
            return allCandidates.Where(c => !VisitedSet.Contains(c)).ToList();
        }

        public bool ShouldContinue(List<string> unvisitedCandidates)
        {
            if (IsCompleted) return false;
            if (CurrentStep >= MaxSteps) return false;
            if (unvisitedCandidates.Count == 0)
            {
                IsDeadEnd = true;
                return false;
            }
            return true;
        }

        public string GetPathSummary()
        {
            var path = VisitedPath.Count > 0
                ? string.Join(" → ", VisitedPath)
                : "(空路径)";
            return $"导航 [{ContextId}]: {path} (共 {CurrentStep} 步)";
        }

        public void Reset()
        {
            VisitedPath.Clear();
            VisitedSet.Clear();
            Steps.Clear();
            CurrentNode = StartNode;
            IsCompleted = false;
            IsDeadEnd = false;
        }
    }
}
