
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Reference;

public class OutputTargetRule : ValidationRuleBase
{
    public OutputTargetRule()
        : base(
            "OutputTarget",
            "校验 bindings.output 引用的控件名是否在配置树中存在",
            ValidationRuleCategory.Reference)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var regionKeys = CollectRegionKeys(config, cancellationToken);

        if (context.ControlTree == null)
        {
            // 未配置提供者 = 控件名校验未启用；区域目标仍按声明面校验（结构事实，不依赖控件树）。
            diagnostics.Add(
                ValidationDiagnostic.CreateInfo(
                    RuleName,
                    "控件树提供者未配置，跳过控件类输出目标校验"));
        }

        var outputs = JsonTreeWalker.FindBindingsOutput(config, cancellationToken);
        foreach (var (outputTarget, jsonPath) in outputs)
        {
            var raw = outputTarget?.Trim() ?? string.Empty;
            if (RegionPrefix.Length > 0 && raw.StartsWith(RegionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = raw.Substring(RegionPrefix.Length).Trim();
                if (string.IsNullOrEmpty(key) || !regionKeys.Contains(key))
                {
                    var location = new SourceLocation(
                        context.FilePath ?? "unknown",
                        jsonPath);

                    diagnostics.Add(
                        ValidationDiagnostic.CreateError(
                            RuleName,
                            $"output 区域目标 '{raw}' 未在页面 regions 中声明")
                        .WithLocation(location)
                        .WithSuggestion($"在 htmlPage.regions 声明 {{\"key\":\"{key}\"}}，或改用已声明的区域键"));
                }
                continue;
            }

            var controlName = raw.StartsWith(HostPrefix, StringComparison.OrdinalIgnoreCase)
                ? raw.Substring(HostPrefix.Length).Trim()
                : raw;

            if (context.ControlTree is null)
                continue;

            if (!context.ControlTree.HasControl(controlName))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath);

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"output 目标控件 '{raw}' 不存在")
                    .WithLocation(location)
                    .WithSuggestion($"确保控件名 '{controlName}' 在配置树中已定义，或检查是否有拼写错误"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private const string RegionPrefix = "region:";
    private const string HostPrefix = "host:";

    /// <summary>收集页面区域声明键（<c>htmlPage.regions[].key</c>，跨页面/跨层级递归）。
    /// 区域目标合法性 = 声明面结构事实，与控件索引解耦（region 不登记为控件）。</summary>
    private static HashSet<string> CollectRegionKeys(JsonElement root, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root);
        return keys;

        void Walk(JsonElement node)
        {
            ct.ThrowIfCancellationRequested();
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in node.EnumerateObject())
                    {
                        if (prop.Name.Equals("regions", StringComparison.OrdinalIgnoreCase)
                            && prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var region in prop.Value.EnumerateArray())
                            {
                                if (region.ValueKind == JsonValueKind.Object
                                    && region.TryGetProperty("key", out var key)
                                    && key.ValueKind == JsonValueKind.String
                                    && !string.IsNullOrWhiteSpace(key.GetString()))
                                {
                                    keys.Add(key.GetString()!);
                                }
                            }
                        }

                        Walk(prop.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                        Walk(item);
                    break;
            }
        }
    }
}
