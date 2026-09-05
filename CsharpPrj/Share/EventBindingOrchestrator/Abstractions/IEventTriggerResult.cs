
using System;

namespace Share.EventBindingOrchestrator.Abstractions
{
    public interface IEventTriggerResult
    {
        bool IsSuccess { get; }
        object? Value { get; }
        string? ErrorMessage { get; }
        TimeSpan ExecutionTime { get; }
        string? Message { get; }
        string? ControlName { get; }
        string? EventType { get; }
        string? FunctionName { get; }
        string? OutputTarget { get; }
        bool IsWorkflowSwitchEvent { get; }
    }
}
