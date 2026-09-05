
using System;
using System.Collections.Generic;

namespace TConfig.ContextFrame.Abstractions
{
    public interface IContextSnapshot
    {
        string ContextId { get; }

        DateTime SnapshotTime { get; }

        IReadOnlyDictionary<string, object?> Data { get; }
    }

    public interface ISnapshotable
    {
        IContextSnapshot CreateSnapshot();

        void RestoreSnapshot(IContextSnapshot snapshot);
    }
}
