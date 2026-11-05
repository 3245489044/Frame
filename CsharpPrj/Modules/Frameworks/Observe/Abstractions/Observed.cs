using System;

namespace Modules.Observe;

/// <summary>一次可观测读取的结果。<c>Value</c> 为观测投影（可能旧/空），<c>Version</c> 单调版本，<c>IsStale</c> 陈旧标记。</summary>
public sealed record Observed<T>(T? Value, long Version, bool IsStale);
