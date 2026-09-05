
using CommandFramework;
using CommandFramework.Invoke;
using FrameworkAbstractions.ComponentRegistry;
using TConfig;
using WorkFlowMethod.Execution;
using BackEndDriven.UnifiedConfig;

namespace BackEndDriven.WorkFlowMethodBridge;

public class BackEndDrivenBuilder
{
    private readonly CommandFrameworkOptions _options;

    public BackEndDrivenBuilder(CommandFrameworkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public BackEndDrivenBuilder WithWorkflowMethodExecution(
        IEventExecutionService executionService)
    {
        if (executionService == null)
            throw new ArgumentNullException(nameof(executionService));

        var controlValueAccessor = _options.ControlValueAccessor;
        if (controlValueAccessor == null)
            throw new InvalidOperationException(
                "IControlValueAccessor is not configured. " +
                "Set options.ControlValueAccessor before calling WithWorkflowMethodExecution().");

        var controlFinder = _options.ControlFinder;

        _options.ParameterExecutionWorkflow = new WorkflowMethodExecutionWorkflow(
            executionService, controlValueAccessor, controlFinder);

        return this;
    }


    public BackEndDrivenBuilder WithWorkflowMethodExecution()
    {
        var executionService = AppServiceProvider.Resolve<IEventExecutionService>();
        if (executionService == null)
            throw new InvalidOperationException(
                "IEventExecutionService is not registered in AppServiceProvider. " +
                "Ensure WorkFlowMethod is initialized (WorkFlowMethodUsage.EnsureInitialized()) " +
                "before calling WithWorkflowMethodExecution().");

        return WithWorkflowMethodExecution(executionService);
    }

   public BackEndDrivenBuilder WithParameterExecutionWorkflow(
        IParameterExecutionWorkflow workflow)
    {
        _options.ParameterExecutionWorkflow = workflow
            ?? throw new ArgumentNullException(nameof(workflow));
        return this;
    }

    public IParameterExecutionWorkflow? BuildWorkflow() => _options.ParameterExecutionWorkflow;

    public BackEndDrivenBuilder WithUnifiedConfig(string configPath, ITrConfigRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        var loader = new UnifiedConfigLoader(registry);
        loader.LoadAndDispatch(configPath);

        return this;
    }

    public CommandFrameworkOptions Apply() => _options;
}
