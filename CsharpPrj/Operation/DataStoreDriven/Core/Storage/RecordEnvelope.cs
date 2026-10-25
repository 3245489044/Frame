using System;
using System.Collections.Generic;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Core;

/// <summary>记录信封单源（U3）：<c>"{kind}:{key}"</c> 主键与 <c>ThreadId</c> 信封列，取代手写字符串拼接。</summary>
public static class RecordEnvelope
{
    /// <summary>记录主键（<c>kind:key</c>）。</summary>
    public static string RecordId(string kind, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(key);
        return kind + ":" + key;
    }

    /// <summary>线程信封字段（ThreadId 随文档持久化）。</summary>
    public static KeyValuePair<string, UsharpValue> ThreadId(string threadId)
        => new("ThreadId", UsharpScalar.Str(threadId));

    /// <summary>字符串字段信封。</summary>
    public static KeyValuePair<string, UsharpValue> Text(string name, string? value)
        => new(name, UsharpScalar.Str(value ?? string.Empty));

    /// <summary>中立值字段信封（已解析负载）。</summary>
    public static KeyValuePair<string, UsharpValue> Value(string name, UsharpValue value)
        => new(name, value);
}
