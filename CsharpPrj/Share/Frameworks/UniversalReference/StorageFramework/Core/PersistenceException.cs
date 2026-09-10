using System;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>持久化层统一异常。</summary>
public class PersistenceException : Exception
{
    public PersistenceException(string message) : base(message) { }

    public PersistenceException(string message, Exception inner) : base(message, inner) { }
}
