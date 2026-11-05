using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Diagnostics;

namespace Modules.Declaration;

/// <summary>
/// 声明→绑定闭合判据（唯一实现，R1/R2/R3）。
/// <para>R1-a 函数必须已注册；R1-c 声明端口必须 ⊆ 实现产出键；R2 注册未声明 = 孤儿实现。</para>
/// </summary>
public static class DeclarationClosureValidator
{
    /// <summary>R1：声明 → 实现（无悬空）。</summary>
    public static IReadOnlyList<DeclarationViolation> Verify(
        DeclarationCatalog catalog, ImplementationCatalog impl)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(impl);

        var violations = new List<DeclarationViolation>();

        foreach (var node in catalog.Nodes)
        {
            if (!impl.IsRegistered(node.Function))
            {
                violations.Add(new DeclarationViolation(
                    KeyReasonCodes.DeclUnbound,
                    $"node={node.NodeKey}, fn={node.Function}",
                    node.NodeKey,
                    node.Function));
                continue;
            }

            if (!impl.TryGetProduced(node.Function, out var produced)) continue;

            var missing = node.Outports
                .Where(port => !produced.Contains(port, StringComparer.Ordinal))
                .ToArray();
            if (missing.Length == 0) continue;

            violations.Add(new DeclarationViolation(
                KeyReasonCodes.DeclUnboundPort,
                $"node={node.NodeKey}, port=[{string.Join(",", missing)}], " +
                $"declared=[{string.Join(",", node.Outports)}], produced=[{string.Join(",", produced)}]",
                node.NodeKey,
                node.Function));
        }

        foreach (var step in catalog.Steps)
        {
            if (impl.IsRegistered(step.Function)) continue;
            violations.Add(new DeclarationViolation(
                KeyReasonCodes.DeclUnbound,
                $"flow={step.FlowId}, step={step.StepKey}, fn={step.Function}",
                step.StepKey,
                step.Function));
        }

        return violations;
    }

    /// <summary>R2：实现 → 声明（无孤儿）。豁免集由调用方声明（内置函数/工具面），禁调用点自评。</summary>
    public static IReadOnlyList<DeclarationViolation> VerifyOrphans(
        DeclarationCatalog catalog,
        ImplementationCatalog impl,
        IReadOnlyCollection<string>? exemptions = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(impl);

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in catalog.Nodes) declared.Add(node.Function);
        foreach (var step in catalog.Steps) declared.Add(step.Function);

        var exempt = new HashSet<string>(StringComparer.Ordinal);
        if (exemptions != null)
        {
            foreach (var name in exemptions)
            {
                if (!string.IsNullOrWhiteSpace(name)) exempt.Add(name.Trim());
            }
        }

        return impl.RegisteredFunctions
            .Where(name => !declared.Contains(name) && !exempt.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new DeclarationViolation(
                KeyReasonCodes.DeclOrphanImpl,
                $"impl={name}",
                name,
                name))
            .ToArray();
    }

    /// <summary>R3：指纹对齐（声明源指纹 = 清单指纹）。</summary>
    public static DeclarationViolation? VerifyFingerprint(string sourceFingerprint, BindingManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(sourceFingerprint) || !manifest.IsFrozen) return null;
        if (string.Equals(sourceFingerprint, manifest.Fingerprint, StringComparison.Ordinal)) return null;

        return new DeclarationViolation(
            KeyReasonCodes.DeclFingerprintMismatch,
            $"expected={manifest.Fingerprint}, actual={sourceFingerprint}",
            manifest.Fingerprint,
            null);
    }

    /// <summary>姿态裁决：Report ⇒ 返回清单不抛；Throw ⇒ 有违规即抛（fail-fast）。</summary>
    public static IReadOnlyList<DeclarationViolation> Enforce(
        IReadOnlyList<DeclarationViolation> violations, ClosureStrictness strictness, string scope)
    {
        ArgumentNullException.ThrowIfNull(violations);
        if (strictness == ClosureStrictness.Throw && violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"声明未闭合（{scope}）：{string.Join(" | ", violations.Select(v => $"{v.Code}:{v.Detail}"))}");
        }

        return violations;
    }
}
