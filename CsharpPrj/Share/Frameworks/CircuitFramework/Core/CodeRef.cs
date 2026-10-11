namespace Share.CircuitFramework.Core;

/// <summary>
/// 代码实体引用（G2）：把电路节点 / 装配步骤绑定到真实代码实体（类型 + 方法 + 可选签名），
/// 使「函数流动图」可从代码组织**直接投影**并与真实代码**对拍** —— 消除「手工声明 vs 自动抽取」之分
/// （声明即代码组织的结构事实；图是其投影）。
/// <para>通用件：零领域类型（类型/方法名由调用方给出；框架不反射业务程序集，仅承载结构引用）。</para>
/// </summary>
/// <param name="Type">类型名（含命名空间；如 <c>Agent.Launcher.TaskPoolHosting</c>）。</param>
/// <param name="Method">方法名（如 <c>BuildTaskPoolStoreAsync</c>）。</param>
/// <param name="Assembly">程序集名（可选；供跨程序集解析与对拍）。</param>
/// <param name="SignatureHash">签名指纹（可选；内容指纹，参与缓存键与「声明=代码」对拍）。</param>
/// <param name="PortTypes">端口名 → 类型名（端口类型语义，G6；缺省 null = 无类型信息）。</param>
/// <param name="ReturnType">返回类型名（可选；端口类型语义的补充来源）。</param>
public sealed record CodeRef(
    string Type,
    string Method,
    string? Assembly = null,
    string? SignatureHash = null,
    IReadOnlyDictionary<string, string>? PortTypes = null,
    string? ReturnType = null)
{
    /// <summary>可解析（类型与方法名均非空）—— 代码实体应有的最小结构事实。</summary>
    public bool IsResolvable => !string.IsNullOrWhiteSpace(Type) && !string.IsNullOrWhiteSpace(Method);

    /// <summary>显示名：<c>Type.Method</c>（带程序集前缀）。</summary>
    public string DisplayName
        => string.IsNullOrWhiteSpace(Assembly) ? $"{Type}.{Method}" : $"{Assembly}:{Type}.{Method}";

    /// <summary>便捷构造：带端口类型表。</summary>
    public static CodeRef Of(string type, string method, IReadOnlyDictionary<string, string> portTypes)
        => new(type, method, PortTypes: portTypes);

    public override string ToString() => DisplayName;
}
