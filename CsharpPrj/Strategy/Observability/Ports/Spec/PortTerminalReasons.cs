namespace Strategy.Observability.Ports;

/// <summary>
/// 规格不可求值时的**终止原因**（唯一定义源）：装配面按此抛出既有诊断。
/// <para>
/// 纪律（SingleImplementation）：同一句诊断只在此处定义，声明侧（<see cref="PortSpecCompiler"/>）与执行侧
/// （<c>PortExecutor</c> 的构造期校验）共用同一份文本 —— 批 0 实测的 Unbound 文案即由本件保证逐字不变。
/// </para>
/// </summary>
internal static class PortTerminalReasons
{
    /// <summary>脚本族「无行为声明」：script / project / 延迟步骤 / issue 捕获体 / 值载体表达式全空。</summary>
    internal static string ScriptedNoBehavior(string portKey)
        => $"端口 '{portKey}' 声明 scripted 绑定但 script / project / 延迟步骤 / issue 捕获体 / 值载体表达式均为空（无行为可执行）";

    /// <summary>组合族「无调用声明」。</summary>
    internal static string ComposedNoCalls(string portKey)
        => $"端口 '{portKey}' 声明 composed 绑定但未声明任何调用（calls 为空）";

    /// <summary>组合族「无投影字段」。</summary>
    internal static string ComposedNoProject(string portKey)
        => $"端口 '{portKey}' 声明 composed 绑定但未声明任何投影字段（project 为空）";

    /// <summary>组合族「调用引用名重复」。</summary>
    internal static string ComposedDuplicateReference(string portKey, string referenceName)
        => $"端口 '{portKey}' 的调用引用名 '{referenceName}' 重复（投影引用将歧义）";

    /// <summary>反射族「缺 target/method」。</summary>
    internal static string ReflectiveMissingTarget(string portKey)
        => $"端口 '{portKey}' 声明 reflective 绑定但缺少 target/method";
}
