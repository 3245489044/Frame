
using Modules.StateMachine;

namespace Share.StateManagement
{
    public class ExecutionStateMachine : ConditionalStateMachine<ExecutionStatus>, IExecutionStateMachine<ExecutionStatus>
    {
        public ExecutionStateMachine(ExecutionStatus initialState = ExecutionStatus.NotStarted)
            : base(initialState)
        {
        }

        protected override bool IsActiveState(ExecutionStatus state)
        {
            return state == ExecutionStatus.Running ||
                   state == ExecutionStatus.Paused;
        }

        protected override bool IsFinishedState(ExecutionStatus state)
        {
            return state == ExecutionStatus.Completed ||
                   state == ExecutionStatus.Faulted ||
                   state == ExecutionStatus.Terminated ||
                   state == ExecutionStatus.Cancelled;
        }

        public void Reset()
        {
            Reset(ExecutionStatus.NotStarted);
        }

        public new ExecutionStateSnapshot GetSnapshot()
        {
            var snapshot = base.GetSnapshot();
            return new ExecutionStateSnapshot
            {
                Status = snapshot.Status,
                StartTime = snapshot.StartTime,
                EndTime = snapshot.EndTime,
                IsActive = snapshot.IsActive,
                IsFinished = snapshot.IsFinished
            };
        }
    }

    public class ExecutionStateSnapshot
    {
        public ExecutionStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsActive { get; set; }
        public bool IsFinished { get; set; }
        public TimeSpan Duration => EndTime.HasValue
            ? EndTime.Value - StartTime
            : DateTime.UtcNow - StartTime;
    }
}
