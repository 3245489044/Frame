using Plate.DslMethod.Model;

namespace Plate.DslMethod.Logic;

/// <summary>校验问题：绑定级（悬空 target / 缺 impl / 非法 kind 等）。</summary>
public sealed record ValidationIssue(BindingDeclaration? Binding, string Message)
{
    public override string ToString()
        => Binding is null
            ? Message
            : $"[{Binding.SourceFile}:{Binding.LineNumber}] {Binding.Name}：{Message}";
}
