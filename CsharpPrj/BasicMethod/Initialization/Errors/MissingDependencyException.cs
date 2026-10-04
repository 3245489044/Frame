namespace BasicMethod.Initialization.Errors;

/// <summary>
/// 缺前置依赖（装配期错误）——与「运行失败」分层的专用异常。
/// 继承 <see cref="ArgumentNullException"/> 以保持与 <c>ArgumentNullException.ThrowIfNull</c> 的
/// 捕获兼容（红线#1：P1 批量替换须同抛点、同 paramName，且旧 <c>catch (ArgumentNullException)</c> 不回归）。
/// </summary>
public sealed class MissingDependencyException : ArgumentNullException
{
    /// <summary>缺失的端口名集合（供门禁/看板统计装配期缺陷）。</summary>
    public IReadOnlyList<string> MissingPorts { get; }

    /// <summary>单端口缺失（参数入口形态）。</summary>
    public MissingDependencyException(string port)
        : base(port, $"缺少必填端口：{port}")
        => MissingPorts = new[] { port };

    /// <summary>单端口缺失（带自定义消息）。</summary>
    public MissingDependencyException(string port, string message)
        : base(port, message)
        => MissingPorts = new[] { port };

    /// <summary>多端口缺失（装配期一次性报告，非首个即抛）。</summary>
    public MissingDependencyException(string component, IReadOnlyList<string> missing)
        : base(component, $"{component} 缺少必填端口：{string.Join(", ", missing)}")
        => MissingPorts = missing;
}
