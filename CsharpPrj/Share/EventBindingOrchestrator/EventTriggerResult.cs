
using System;
using TConfig;
using ExecutionStatus = TConfig.ExecutionStatus;

namespace Share.EventBindingOrchestrator.Models
{
    public class EventTriggerResult : TConfig.EventResult,
        Modules.EventFramework.Services.IEventTriggerResult,
        Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult
    {
        public string? EventName { get; set; }


        string? Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult.ControlName => ControlName;
        string? Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult.EventType => EventType;
        string? Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult.Message => Message;
        bool Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult.IsWorkflowSwitchEvent => IsWorkflowSwitchEvent;

        public static EventTriggerResult SuccessResult(
            object? value,
            string? controlName = null,
            string? eventType = null,
            string? functionName = null,
            string? outputTarget = null,
            TimeSpan? executionTime = null,
            string? eventName = null)
            => new()
            {
                Status = ExecutionStatus.Completed,
                IsSuccess = true,
                Value = value,
                ControlName = controlName,
                EventType = eventType,
                FunctionName = functionName,
                OutputTarget = outputTarget,
                ExecutionTime = executionTime ?? TimeSpan.Zero,
                EventName = eventName ?? eventType
            };

        public static EventTriggerResult FailureResult(
            string errorMessage,
            string? controlName = null,
            string? eventType = null,
            string? functionName = null,
            TimeSpan? executionTime = null,
            string? eventName = null)
            => new()
            {
                Status = ExecutionStatus.Failed,
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ControlName = controlName,
                EventType = eventType,
                FunctionName = functionName,
                ExecutionTime = executionTime ?? TimeSpan.Zero,
                EventName = eventName ?? eventType
            };
    }
}
