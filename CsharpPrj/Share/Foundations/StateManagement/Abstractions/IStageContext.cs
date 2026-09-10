
using Modules.Pipeline;

namespace Share.StateManagement
{
    public interface IStageContext : IPipelineContext
    {

        IStateStore Data { get; }


        string? CurrentNodeKey { get; set; }

        string? NextNodeKey { get; set; }

        IReadOnlyList<string>? NodeKeys { get; }


        object? CurrentInput { get; set; }

        object? CurrentOutput { get; set; }


        string? SessionId { get; }


        void LogInfo(string stage, string message);

        void LogWarn(string stage, string message);

        void LogError(string stage, string message, Exception? ex = null);

        void LogDebug(string stage, string message);
    }
}
