
#nullable enable
using System;
using System.Threading.Tasks;
using BackEndDriven.Abstractions;
using Share.EventBindingOrchestrator;

namespace BackEndDriven.Pipeline
{
    public class RenderPipelineOrchestrator
    {
        private readonly IRenderPipeline _pipeline;
        private readonly EventBindingOrchestrator? _eventOrchestrator;
        private readonly string _name;
        private bool _disposed;

        public string Name => _name;

        public IRenderPipeline Pipeline => _pipeline;

        public EventBindingOrchestrator? EventOrchestrator => _eventOrchestrator;

        public RenderPipelineOrchestrator(
            IRenderPipeline pipeline,
            EventBindingOrchestrator? eventOrchestrator,
            string? name = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _eventOrchestrator = eventOrchestrator;
            _name = name ?? "RenderPipelineOrchestrator";
        }

        public async Task<(bool eventsBoundSuccess, string? eventsBoundError)> RenderFullAsync(object target, object config)
        {
            ThrowIfDisposed();

            bool eventsBoundSuccess = true;
            string? eventsBoundError = null;

            try
            {
                await _pipeline.ExecuteCreateStageAsync(target, config);

                await _pipeline.ExecuteLayoutStageAsync(target);

                await _pipeline.ExecuteBindEventsStageAsync(target, config);
            }
            catch (Exception ex)
            {
                eventsBoundSuccess = false;
                eventsBoundError = ex.Message;
            }

            return (eventsBoundSuccess, eventsBoundError);
        }

        public Task ExecuteCreateStageAsync(object target, object config)
        {
            ThrowIfDisposed();
            return _pipeline.ExecuteCreateStageAsync(target, config);
        }

        public Task ExecuteLayoutStageAsync(object target)
        {
            ThrowIfDisposed();
            return _pipeline.ExecuteLayoutStageAsync(target);
        }

        public Task ExecuteBindEventsStageAsync(object target, object config)
        {
            ThrowIfDisposed();
            return _pipeline.ExecuteBindEventsStageAsync(target, config);
        }

        public string OutputDiagnosticSnapshot()
        {
            return $"[{_name}] Pipeline={_pipeline.Name}, HasEventOrchestrator={_eventOrchestrator != null}";
        }

        public void DisposeSession(object target)
        {
            ThrowIfDisposed();
            _pipeline.DisposeSession(target);
        }

        public object? GetActiveSession(object target)
        {
            ThrowIfDisposed();
            return _pipeline.GetActiveSession(target);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RenderPipelineOrchestrator));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
