
using Modules.Common.StepCoordination;
using TConfig;

namespace Modules.Pipeline.Initialization;

public class StringStageCoordinator : IStageCoordinator
{
    private readonly Dictionary<string, StageDefinition> _definitions = new();

    private readonly Dictionary<string, IStepCoordinator> _coordinators = new();

    private readonly Dictionary<string, StageState> _states = new();

    private readonly Dictionary<string, DateTime> _startTimes = new();

    private readonly Dictionary<string, DateTime> _completionTimes = new();

    private readonly Dictionary<string, string> _errors = new();

    private readonly List<string> _orderedStages = new();

    private DateTime _startTime;

    private bool _started;

    private bool _completed;

    private readonly object _lock = new();

    public event EventHandler<StageStateChangedEventArgs>? StageStateChanged;

    public void RegisterStage(string stageId, string description, int timeoutMs = 30000)
    {
        lock (_lock)
        {
            if (_started)
                throw new InvalidOperationException("协调器已启动，不能注册新阶段。");

            if (_definitions.ContainsKey(stageId))
                throw new ArgumentException($"阶段 '{stageId}' 已注册。", nameof(stageId));

            var definition = new StageDefinition(stageId, description, timeoutMs);
            _definitions[stageId] = definition;
            _coordinators[stageId] = StepCoordinationExtensions.CreateDefault();
            _states[stageId] = StageState.Pending;
            _orderedStages.Add(stageId);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_definitions.Count == 0)
                throw new InvalidOperationException("没有注册任何阶段，请先调用 RegisterStage。");

            _startTime = DateTime.UtcNow;
            _started = true;

            foreach (var stageId in _orderedStages)
            {
                var state = _states[stageId];
                if (state == StageState.Pending)
                {
                    SetState(stageId, StageState.Running);
                    break;
                }
            }
        }
    }

    public async Task<bool> WaitForStageAsync(string stageId)
    {
        Task<bool> signalTask;
        int timeoutMs;

        lock (_lock)
        {
            if (!_coordinators.TryGetValue(stageId, out var coordinator))
                throw new ArgumentException($"阶段 '{stageId}' 未注册。", nameof(stageId));

            var state = _states[stageId];
            if (state == StageState.Completed)
                return true;
            if (state == StageState.Failed || state == StageState.TimedOut)
                return false;

            timeoutMs = _definitions[stageId].TimeoutMs;

            signalTask = coordinator.GetOrCreateSignal().Task;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var result = await signalTask.WaitAsync(cts.Token);
            return result;
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(stageId, out var state) && state == StageState.Running)
                {
                    SetState(stageId, StageState.TimedOut);
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void CompleteStage(string stageId, bool success = true, string? errorMessage = null)
    {
        IStepCoordinator? coordinator;
        string? nextStage = null;
        List<string> failedStages = new();
        List<Action> pendingEvents = new();

        lock (_lock)
        {
            if (!_coordinators.TryGetValue(stageId, out coordinator))
                throw new ArgumentException($"阶段 '{stageId}' 未注册。", nameof(stageId));

            _completionTimes[stageId] = DateTime.UtcNow;

            if (!_startTimes.ContainsKey(stageId))
            {
                _startTimes[stageId] = DateTime.UtcNow;
            }

            if (success)
            {
                pendingEvents.Add(() => SetState(stageId, StageState.Completed));
            }
            else
            {
                pendingEvents.Add(() => SetState(stageId, StageState.Failed));
                if (!string.IsNullOrEmpty(errorMessage))
                    _errors[stageId] = errorMessage;
            }

            if (success)
            {
                nextStage = GetNextStage(stageId);
                if (nextStage != null)
                {
                    pendingEvents.Add(() => SetState(nextStage, StageState.Running));
                }
                else
                {
                    _completed = true;
                }
            }
            else
            {
                _completed = true;

                var next = GetNextStage(stageId);
                while (next != null)
                {
                    var capturedStage = next;
                    failedStages.Add(capturedStage);
                    pendingEvents.Add(() => SetState(capturedStage, StageState.Failed));
                    next = GetNextStage(next);
                }
            }
        }

        foreach (var evt in pendingEvents)
        {
            evt();
        }

        if (success)
        {
            coordinator.CompleteStep(true);
        }
        else
        {
            coordinator.CompleteStep(false);
        }

        foreach (var failedStage in failedStages)
        {
            if (_coordinators.TryGetValue(failedStage, out var failedCoordinator))
            {
                failedCoordinator.CompleteStep(false);
            }
        }
    }

    public ExecutionStatus GetState(string stageId)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(stageId, out var state))
                return ExecutionStatus.Pending;

            return state switch
            {
                StageState.Pending => TConfig.ExecutionStatus.Pending,
                StageState.Running => TConfig.ExecutionStatus.Running,
                StageState.Completed => TConfig.ExecutionStatus.Completed,
                StageState.Failed => TConfig.ExecutionStatus.Failed,
                StageState.TimedOut => TConfig.ExecutionStatus.TimedOut,
                _ => TConfig.ExecutionStatus.Pending
            };
        }
    }

    private string? GetNextStage(string currentStage)
    {
        var index = _orderedStages.IndexOf(currentStage);
        if (index < 0 || index >= _orderedStages.Count - 1)
            return null;

        return _orderedStages[index + 1];
    }

    private void SetState(string stageId, StageState newState)
    {
        StageState oldState;
        StageDefinition? definition;
        EventHandler<StageStateChangedEventArgs>? handler;

        lock (_lock)
        {
            oldState = _states[stageId];
            if (oldState == newState)
                return;

            _states[stageId] = newState;

            if (newState == StageState.Running && !_startTimes.ContainsKey(stageId))
            {
                _startTimes[stageId] = DateTime.UtcNow;
            }

            definition = _definitions[stageId];
            handler = StageStateChanged;
        }

        if (handler != null && definition != null)
        {
            handler(this, new StageStateChangedEventArgs(
                stageId, definition.Description, oldState, newState));
        }
    }

    public class StageStateChangedEventArgs : EventArgs
    {
        public string StageId { get; }

        public string Description { get; }

        public StageState OldState { get; }

        public StageState NewState { get; }

        public StageStateChangedEventArgs(
            string stageId,
            string description,
            StageState oldState,
            StageState newState)
        {
            StageId = stageId;
            Description = description;
            OldState = oldState;
            NewState = newState;
        }
    }
}
