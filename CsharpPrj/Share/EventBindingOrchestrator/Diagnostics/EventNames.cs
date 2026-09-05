
namespace Share.EventBindingOrchestrator.Diagnostics;

public static class EventNames
{

    public const string PipelineExecuteWithWalkStart = "pipeline.execute_with_walk.start";

    public const string PipelineExecuteWithWalkComplete = "pipeline.execute_with_walk.complete";


    public const string EventBinderForEachBoundEvent = "eventbinder.transform.for_each_bound_event";

    public const string EventBinderEventTriggered = "eventbinder.event.triggered";

    public const string EventBinderReentrySkip = "eventbinder.reentry.skip";

    public const string EventBinderBoundViaEventBus = "eventbinder.bound_via_eventbus";

    public const string EventBinderTryBindEventEventNotFound = "eventbinder.try_bind_event.event_not_found";

    public const string EventBinderSkipNullControl = "eventbinder.skip_null_control";

    public const string EventBinderTryBindEventEntry = "eventbinder.try_bind_event.entry";

    public const string EventBinderTryBindEventReflectionFailed = "eventbinder.try_bind_event.reflection_failed";

    public const string EventBinderUnbindFailed = "eventbinder.unbind_failed";


    public const string OrchestratorLoadAndBindStart = "orchestrator.load_and_bind.start";


    public const string ControlFinderConfigValidationWarning = "controlfinder.config_validation_warning";
}
