using BasicMethod.Initialization.Errors;

namespace BasicMethod.Initialization.Guards;

/// <summary>
/// 装配期 / 参数入口的唯一处置门面（区分「缺前置」与「运行失败」）。
/// 命名 <c>PortGuard</c> 以区别于既有 <c>Share.TaskStatusPool.Loops.Guard</c> 与
/// <c>GraphFramework.Helpers.Guard</c>——禁第三同名平行实现（Strategy.SingleImplementation）。
/// </summary>
public static class PortGuard
{
    /// <summary>
    /// T：必填为空 → fail-fast。行为等价于 <c>ArgumentNullException.ThrowIfNull(value)</c>（同抛点、同 paramName）。
    /// </summary>
    public static T Required<T>(T? value, string name) where T : class
        => value ?? throw new MissingDependencyException(name);

    /// <summary>T：必填为空 → fail-fast（带自定义消息，兼容原 <c>throw new ArgumentNullException(name, msg)</c> 形态）。</summary>
    public static T Required<T>(T? value, string name, string message) where T : class
        => value ?? throw new MissingDependencyException(name, message);

    /// <summary>装配期：端口集合校验，一次性报告全部缺失（非首个即抛）。</summary>
    public static void PortsComplete(string component, IReadOnlyList<string> missing)
    {
        if (missing is { Count: > 0 })
            throw new MissingDependencyException(component, missing);
    }

    /// <summary>N：可选端口，缺省用命名空对象。</summary>
    public static T Optional<T>(T? value, T nullObject) where T : class
        => value ?? nullObject;
}
