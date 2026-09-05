
using System.Collections.Concurrent;
using TConfig;

using IParsedCommand = CommandFramework.Abstractions.IParsedCommand;
using CommandCriticality = CommandFramework.Abstractions.CommandCriticality;
using INonEventCommandRegistry = CommandFramework.Abstractions.INonEventCommandRegistry;
using NonEventCommandValidationResult = CommandFramework.Abstractions.NonEventCommandValidationResult;

namespace FunctionStorage.Abstractions
{


    public interface IFunctionUnit
    {
        string Name { get; }

        string? Description { get; }

        Task<ExecutionResult> ExecuteAsync(object? input);
    }


    public interface INonEventCommandHandler
    {
        void HandleLoad(IExecutionContext context);

        void HandleClose(IExecutionContext context);

        void HandleShow(IExecutionContext context);

        void HandleLook(IExecutionContext context);

        void HandleDiscover(IExecutionContext context);

        void HandleList(IExecutionContext context);

        void HandleValidate(IExecutionContext context);

        void HandleExit(IExecutionContext context);

        void HandleSideload(IExecutionContext context);
    }


    public interface INonEventCommandStrategy
    {
        string CommandName { get; }

        string Description { get; }

        CommandCriticality Criticality { get; }

        void Execute(IExecutionContext context);
    }
}
