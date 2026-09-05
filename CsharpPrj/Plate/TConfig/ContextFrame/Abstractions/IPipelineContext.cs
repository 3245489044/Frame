
using System;
using System.Collections.Generic;

namespace TConfig.ContextFrame.Abstractions
{
    public interface IPipelineContext : IContext
    {

        string Mode { get; set; }


        IReadOnlyDictionary<string, bool> StageSuccess { get; }

        IReadOnlyDictionary<string, string?> StageErrors { get; }

        IDictionary<string, object?> SharedData { get; }

        bool HasError { get; }

        string? FirstError { get; }


        void SetStageResult(string stageName, bool success, string? error = null);

        bool GetStageSuccess(string stageName);

        string? GetStageError(string stageName);

        void Reset();
    }
}
