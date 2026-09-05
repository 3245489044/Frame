using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Graph;

/// <summary>引用边载荷：<see cref="IResourceReferenceStore"/> 契约仅携带 RefType + Strength。</summary>
public sealed record ReferenceEdgeData(string RefType, double Strength);
