#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.Gather;

/// <summary>
/// 聚拢组合模块（**合并子模块声明**）：把两个及以上 <see cref="IGatherModule"/> 的声明与实现收拢成一个模块。
/// <para>装配期判据（任一不满足 ⇒ 抛 <see cref="GatherClosureViolationException"/>，禁静默覆盖）：</para>
/// <list type="bullet">
/// <item>C1 同名同 Kind 声明冲突（含同域双 Need）；</item>
/// <item>C2 同一 location 双 producer（SingleImplementation）；</item>
/// <item>C3 子模块不得自行声明 Class（唯一作者 = <see cref="GatherRegistry"/>）。</item>
/// </list>
/// <para>两阶段：先全量收集与校验，再回放；冲突时组合 builder 保持未被部分写入。</para>
/// </summary>
public sealed class CompositeGatherModule : IGatherModule
{
    private readonly IGatherModule[] _modules;

    /// <summary>构造组合模块。</summary>
    /// <param name="name">组合模块名（受控命名；作为 Class 名）。</param>
    /// <param name="modules">子模块（至少一个）。</param>
    public CompositeGatherModule(string name, params IGatherModule[] modules)
    {
        if (!GatherSchema.IsValidName(name))
        {
            throw new ArgumentException(
                $"组合模块名非法：'{name}'（须匹配 {GatherSchema.NamePattern}）",
                nameof(name));
        }

        if (modules is null || modules.Length == 0)
        {
            throw new ArgumentException("组合模块至少需要一个子模块", nameof(modules));
        }

        foreach (var module in modules)
        {
            if (module is null) throw new ArgumentException("子模块不得为 null", nameof(modules));
        }

        Name = name;
        _modules = modules;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var children = new List<GatherModuleBuilder>(_modules.Length);
        foreach (var module in _modules)
        {
            var child = new GatherModuleBuilder();
            module.Declare(child);
            children.Add(child);
        }

        // 阶段一：全量校验（无写入）。
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var locations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in children)
        {
            foreach (var declaration in child.Declarations)
            {
                if (declaration.Kind == GatherDeclKind.Class)
                {
                    throw new GatherClosureViolationException(
                        $"[C3] 子模块不得声明 Class（'{declaration.Name}'；唯一作者 = GatherRegistry）");
                }

                var identity = declaration.Kind + ":" + declaration.Name;
                if (!identities.Add(identity))
                {
                    throw new GatherClosureViolationException(
                        $"[C1] 组合声明冲突：{identity}（同域双声明 / 同名同 Kind）");
                }
            }

            foreach (var provider in child.Providers)
            {
                if (!locations.Add(provider.Key))
                {
                    throw new GatherClosureViolationException(
                        $"[C2] location '{provider.Key}' 有多个 producer（违反 SingleImplementation）");
                }
            }
        }

        // 阶段二：回放到组合 builder（顺序 = 子模块声明序）。
        foreach (var child in children)
        {
            foreach (var declaration in child.Declarations)
            {
                Replay(builder, declaration);
            }

            foreach (var provider in child.Providers)
            {
                builder.Provide(provider.Key, provider.Value);
            }
        }
    }

    private static void Replay(GatherModuleBuilder builder, GatherDecl declaration)
    {
        switch (declaration.Kind)
        {
            case GatherDeclKind.Need:
                builder.Need(
                    declaration.Name,
                    declaration.Scope ?? string.Empty,
                    declaration.Location ?? string.Empty);
                break;

            case GatherDeclKind.Stage:
                builder.Stage(
                    declaration.Name,
                    declaration.Deps ?? Array.Empty<string>());
                break;

            case GatherDeclKind.Produce:
                builder.Produce(
                    declaration.Name,
                    declaration.Type ?? string.Empty,
                    declaration.Location ?? string.Empty,
                    declaration.Crit ?? string.Empty,
                    declaration.Recompute);
                break;

            case GatherDeclKind.Function:
                builder.Function(declaration.Name, declaration.Location ?? string.Empty);
                break;

            case GatherDeclKind.Variable:
                builder.Variable(
                    declaration.Name,
                    declaration.Scope ?? string.Empty,
                    declaration.Type ?? string.Empty);
                break;

            default:
                throw new GatherClosureViolationException(
                    $"[C3] 未知声明种类：{declaration.Kind}（无法回放）");
        }
    }
}
