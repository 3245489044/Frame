
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TConfig.Abstractions
{

    public class RenderCompletedEventArgs : EventArgs
    {
        public string PipelineName { get; }

        public bool Success { get; }

        public TimeSpan Duration { get; }

        public string? ErrorMessage { get; }

        public RenderCompletedEventArgs(string pipelineName, bool success, TimeSpan duration, string? error = null)
        {
            PipelineName = pipelineName;
            Success = success;
            Duration = duration;
            ErrorMessage = error;
        }
    }


    public interface IRenderSession
    {
        string SessionId { get; }

        object? Target { get; }

        object? Config { get; }

        CancellationToken CancellationToken { get; }

        Dictionary<string, object> Items { get; }

        bool IsCompleted { get; }

        Task ExecuteStageAsync(string stageName, Func<Task> stageAction);

        void AddDiagnostic(string key, object value);
    }


    public interface IRenderStage
    {
        string Name { get; }

        int Order { get; }

        Task ExecuteAsync(IRenderSession session);
    }


    public interface IRenderPipeline
    {
        string Name { get; }

        Task RenderAsync(object target, object config);

        Task ExecuteCreateStageOnlyAsync(object target, object config);

        Task ExecuteCreateStageAsync(object target, object config);

        Task ExecuteLayoutStageAsync(object target);

        Task ExecuteBindEventsStageAsync(object target, object config);

        Task ApplyLayoutAsync(object target);

        Task BindEventsAsync(object target);

        object? GetActiveSession(object target);

        void DisposeSession(object target);

        event EventHandler<RenderCompletedEventArgs>? RenderCompleted;
    }
}
