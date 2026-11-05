
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Providers;
using ValidationFramework.Utilities;

namespace ValidationFramework.Contracts;

public interface IScopeProvider
{
    string? GetObjectScope(string objectName);
}

public class FunctionContractRule : ValidationRuleBase
{
    public FunctionContractRule()
        : base("FunctionContract",
            "校验函数调用参数数量与注册签名的匹配",
            ValidationRuleCategory.Contract)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var signatureProvider = context.GetProvider<IContractSignatureProvider>();
        if (signatureProvider == null)
        {
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var filePath = context.FilePath ?? "unknown";

        var references = JsonReferenceValidator.FindStringReferencesInContainerArrays(config, "events");

        JsonReferenceValidator.ValidateExistence(
            diagnostics,
            RuleName,
            filePath,
            references,
            signatureProvider.HasFunction,
            signatureProvider.GetFunctions,
            "函数",
            cancellationToken);

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}

public class WorkflowLegalityRule : ValidationRuleBase
{
    public WorkflowLegalityRule()
        : base("WorkflowLegality",
            "校验工作流定义中步骤函数的注册存在性",
            ValidationRuleCategory.Contract)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var objectRegistry = context.GetProvider<IObjectRegistry>();
        if (objectRegistry == null)
        {
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var filePath = context.FilePath ?? "unknown";

        foreach (var (_, wfElement, _) in JsonTreeWalker.FindProperties(config, "Workflows"))
        {
            if (wfElement.ValueKind != JsonValueKind.Object) continue;

            var references = JsonReferenceValidator.FindStringPropertyReferences(wfElement, "FunctionName");

            JsonReferenceValidator.ValidateExistence(
                diagnostics,
                RuleName,
                filePath,
                references,
                objectRegistry.HasItem,
                objectRegistry.GetItems,
                "函数",
                cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}

public class ScopeGuardRule : ValidationRuleBase
{
    public ScopeGuardRule()
        : base("ScopeGuard",
            "校验对象声明作用域与引用作用域匹配",
            ValidationRuleCategory.Contract)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var scopeProvider = context.GetProvider<IScopeProvider>();
        var objectRegistry = context.GetProvider<IObjectRegistry>();

        if (scopeProvider == null && objectRegistry == null)
        {
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var filePath = context.FilePath ?? "unknown";

        foreach (var (_, bindingsValue, bindingsPath) in JsonTreeWalker.FindProperties(config, "bindings"))
        {
            if (bindingsValue.ValueKind != JsonValueKind.Object) continue;

            var scope = JsonReferenceValidator.ExtractScopeFromPath(config, bindingsPath)
                ?? "default";

            foreach (var bindingProp in bindingsValue.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceRefs = JsonReferenceValidator.CollectSourceReferences(
                    bindingProp.Value, $"{bindingsPath}.{bindingProp.Name}");

                ValidateSourceReferences(
                    diagnostics,
                    filePath,
                    scope,
                    sourceRefs,
                    scopeProvider,
                    objectRegistry);
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private static void ValidateSourceReferences(
        ICollection<ValidationDiagnostic> diagnostics,
        string filePath,
        string scope,
        IReadOnlyList<(string Name, string Path)> sourceRefs,
        IScopeProvider? scopeProvider,
        IObjectRegistry? objectRegistry)
    {
        foreach (var (name, path) in sourceRefs)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (objectRegistry != null && !objectRegistry.HasItem(name))
            {
                JsonReferenceValidator.ValidateExistence(
                    diagnostics,
                    "ScopeGuard",
                    filePath,
                    (name, path),
                    objectRegistry.HasItem,
                    objectRegistry.GetItems,
                    "对象");
                continue;
            }

            if (scopeProvider != null)
            {
                var declaredScope = scopeProvider.GetObjectScope(name);
                if (declaredScope == null)
                {
                    diagnostics.Add(
                        ValidationDiagnostic.Warning(
                            "ScopeGuard",
                            $"对象 '{name}' 的作用域未知，无法校验与 '{scope}' 作用域的一致性",
                            new SourceLocation(filePath, path),
                            "在作用域提供者中补充该对象的声明作用域"));
                }
                else if (!string.Equals(declaredScope, scope, StringComparison.Ordinal))
                {
                    diagnostics.Add(
                        ValidationDiagnostic.Error(
                            "ScopeGuard",
                            $"对象 '{name}' 声明于作用域 '{declaredScope}'，" +
                            $"被 '{scope}' 作用域的绑定引用",
                            new SourceLocation(filePath, path),
                            $"将引用移至 '{declaredScope}' 作用域或调整对象声明"));
                }
            }
        }
    }
}