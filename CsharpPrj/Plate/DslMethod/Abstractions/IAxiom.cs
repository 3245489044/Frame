namespace Plate.DslMethod.Abstractions;

/// <summary>
/// 公理标记接口：一条公理一个实现类（编译期一等代码，一公理一文件、一层级一文件夹）。
/// 生成类由 <c>AxiomCodeGenerator</c> 从 prompts/*.md 反转生成；扫描由 <c>AxiomScanner</c> 反射索引。
/// </summary>
public interface IAxiom
{
    /// <summary>公理符号名（定义点唯一主键，对齐 dsl.md §4.7 锚点 ID）。</summary>
    string AxiomName { get; }
}
