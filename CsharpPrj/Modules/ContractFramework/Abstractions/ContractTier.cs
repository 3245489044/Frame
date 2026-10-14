namespace ContractFramework.Abstractions;

/// <summary>
/// 契约执行层级（四检查点，每契约恰一层；层次越靠前越便宜）。
/// <para>对应文档 §5.1：Build=编译/生成期 · Assembly=组合根装配期 · Gate=门禁期 · Runtime=运行期。</para>
/// </summary>
public enum ContractTier
{
    /// <summary>编译/生成期：源码 / AST / csproj / 模型（Roslyn 分析器、源生成器、MSBuild Target）。</summary>
    Build = 0,

    /// <summary>组合根装配期：已声明配置 / 注册表 / 模板文本（<c>IContractHost</c> 直调，单一抛出点）。</summary>
    Assembly = 1,

    /// <summary>门禁期（CI/本地）：仓库文件 / 生成产物（<c>PowerShellScript/Operation/DSL/Check-*.ps1</c>）。</summary>
    Gate = 2,

    /// <summary>运行期：实时状态 / DOM / 计数（探针 → 落盘 + 自愈）。</summary>
    Runtime = 3,
}
