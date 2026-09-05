
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Core
{
    public class PipelineContextBase : ContextBase, IPipelineContext
    {

        private readonly ConcurrentDictionary<string, bool> _stageSuccess = new();
        private readonly ConcurrentDictionary<string, string?> _stageErrors = new();
        private readonly ConcurrentDictionary<string, object?> _sharedData = new();


        public string Mode { get; set; } = "default";

        public IReadOnlyDictionary<string, bool> StageSuccess => _stageSuccess;

        public IReadOnlyDictionary<string, string?> StageErrors => _stageErrors;

        public IDictionary<string, object?> SharedData => _sharedData;

        public bool HasError
        {
            get
            {
                foreach (var kvp in _stageSuccess)
                {
                    if (!kvp.Value) return true;
                }
                return false;
            }
        }

        public string? FirstError
        {
            get
            {
                foreach (var kvp in _stageErrors)
                {
                    if (kvp.Value != null) return kvp.Value;
                }
                return null;
            }
        }


        public PipelineContextBase(string? id = null, IContext? parent = null)
            : base(id, parent)
        {
        }


        public void SetStageResult(string stageName, bool success, string? error = null)
        {
            ThrowIfDisposed();
            _stageSuccess[stageName] = success;

            if (!success && error != null)
                _stageErrors[stageName] = error;
        }

        public bool GetStageSuccess(string stageName)
        {
            return _stageSuccess.TryGetValue(stageName, out var success) ? success : true;
        }

        public string? GetStageError(string stageName)
        {
            return _stageErrors.TryGetValue(stageName, out var error) ? error : null;
        }

        public void Reset()
        {
            ThrowIfDisposed();
            _stageSuccess.Clear();
            _stageErrors.Clear();
            _sharedData.Clear();
        }


        public override void Dispose()
        {
            Reset();
            base.Dispose();
        }
    }
}
