
using System.Text.Json;
using BasicMethod.Block;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Engine;

public sealed class RuleBlockContext
{
    public required JsonElement Config { get; init; }

    public required IValidationContext ValidationContext { get; init; }
}

public class RuleBlockAdapter : BlockBase
{
    public const string DiagnosticsKey = "ValidationFramework.Diagnostics";

    private readonly IValidationRule _rule;

    public RuleBlockAdapter(IValidationRule rule)
        : base(rule.RuleName, rule.Description)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
    }

    public override async Task<BlockResult> ExecuteAsync(
        BlockContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRuleContext(context, out var ruleCtx) || ruleCtx == null)
        {
            return BlockResult.Fail("缺少 RuleBlockContext，无法执行校验规则");
        }

        var diagnostics = await _rule.ValidateAsync(
            ruleCtx.Config, ruleCtx.ValidationContext, cancellationToken);

        CollectDiagnostics(context, diagnostics);

        return BlockResult.Ok($"规则 '{_rule.RuleName}' 校验完成，诊断 {diagnostics.Count} 条");
    }

    public static bool TryGetRuleContext(BlockContext context, out RuleBlockContext? ruleCtx)
    {
        if (context.Data.TryGetValue(RuleBlockContextKey, out var raw)
            && raw is RuleBlockContext typed)
        {
            ruleCtx = typed;
            return true;
        }
        ruleCtx = null;
        return false;
    }

    public static void SetRuleContext(BlockContext context, RuleBlockContext ruleCtx)
        => context.Data[RuleBlockContextKey] = ruleCtx;

    public static IReadOnlyList<ValidationDiagnostic> CollectDiagnosticsFrom(
        BlockContext context)
    {
        if (context.Data.TryGetValue(DiagnosticsKey, out var raw)
            && raw is List<ValidationDiagnostic> list)
        {
            return list.AsReadOnly();
        }
        return Array.Empty<ValidationDiagnostic>();
    }

    private const string RuleBlockContextKey = "ValidationFramework.RuleBlockContext";

    private static void CollectDiagnostics(
        BlockContext context,
        IEnumerable<ValidationDiagnostic> diagnostics)
    {
        if (!context.Data.TryGetValue(DiagnosticsKey, out var raw)
            || raw is not List<ValidationDiagnostic> list)
        {
            list = new List<ValidationDiagnostic>();
            context.Data[DiagnosticsKey] = list;
        }
        list.AddRange(diagnostics);
    }
}