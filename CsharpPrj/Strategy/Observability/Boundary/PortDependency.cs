namespace Strategy.Observability.Boundary;

/// <summary>
/// 端口依赖声明（E9）：本端口被调用时，须已装配的其它端口。
/// 既是装配校验依据（缺依赖 → fail-fast），也是测试侧替身绑定的生成依据。
/// </summary>
public sealed record PortDependency(string PortKey, bool Required = true);
