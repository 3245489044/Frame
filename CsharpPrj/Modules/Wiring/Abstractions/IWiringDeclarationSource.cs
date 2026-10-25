using Modules.Wiring.Model;

namespace Modules.Wiring.Abstractions;

/// <summary>
/// 声明来源（声明→模型的**唯一**入口）。
/// <para>ushar / JSON / 数据库等载体各自的适配器实现本接口，框架不解析任何具体载体语法
/// （禁第二套行解析器，<c>Strategy.SingleImplementation</c>）。</para>
/// </summary>
public interface IWiringDeclarationSource
{
    /// <summary>来源标识（文件集标识 / 逻辑名；参与指纹）。</summary>
    string SourceId { get; }

    IReadOnlyList<FormSpec> Forms { get; }

    IReadOnlyList<BoxSpec> Boxes { get; }

    IReadOnlyList<WireSpec> Wires { get; }

    IReadOnlyList<AdapterSpec> Adapters { get; }

    IReadOnlyList<ProfileSpec> Profiles { get; }
}
