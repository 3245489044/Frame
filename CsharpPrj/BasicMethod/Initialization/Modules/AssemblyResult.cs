namespace BasicMethod.Initialization.Modules;

/// <summary>
/// 装配结果：把「装配缺陷」（<see cref="MissingPorts"/>）与「运行失败」（<see cref="FailedModules"/>）分层归类，
/// 使初始化失败一次性可见、可归因。
/// </summary>
public readonly struct AssemblyResult
{
    /// <summary>是否全部成功。</summary>
    public bool Ok { get; }

    /// <summary>初始化失败的模块名。</summary>
    public IReadOnlyList<string> FailedModules { get; }

    /// <summary>缺失的端口名（装配期缺陷）。</summary>
    public IReadOnlyList<string> MissingPorts { get; }

    public AssemblyResult(bool ok, IReadOnlyList<string> failedModules, IReadOnlyList<string> missingPorts)
    {
        Ok = ok;
        FailedModules = failedModules;
        MissingPorts = missingPorts;
    }

    /// <summary>空成功结果。</summary>
    public static AssemblyResult Success { get; } =
        new(true, Array.Empty<string>(), Array.Empty<string>());
}
