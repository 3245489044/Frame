namespace Modules.Node.Diagnostics;

/// <summary>节点诊断码单一词表（生产与测试共同引用，禁字面量各写一份）。</summary>
public static class NodeDiagnosticCodes
{
    /// <summary>线程约定解析缺位置声明（键文法单源 = 域声明；对象域 = 行键）。</summary>
    public const string PlacementUnbound = "row-key.placement-unbound";
}
