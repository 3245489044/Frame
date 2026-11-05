using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modules.Governance;

/// <summary>资源声明（单位仅作可读标注，内核不据此换算）。</summary>
public sealed record LimitResourceSpec(string Unit = "count");

/// <summary>
/// 限制策略文档（limits.json 的编译产物，纯值、无 IO）。
/// 单源配置：资源/作用域/窗口/动作/优先级/未知策略。
/// </summary>
public sealed class LimitPolicyDocument
{
    public int Version { get; init; } = 1;

    public LimitUnknownPolicy UnknownPolicy { get; init; } = LimitUnknownPolicy.Conservative;

    /// <summary>Fixed 策略下的固定预留量。</summary>
    public long FixedEstimate { get; init; }

    /// <summary>显式声明「无进程级 hard 规则」的审计理由；为空则启动校验拒绝。</summary>
    public string? UnlimitedAuditReason { get; init; }

    public IReadOnlyDictionary<string, LimitResourceSpec> Resources { get; init; }
        = new Dictionary<string, LimitResourceSpec>(StringComparer.Ordinal);

    public IReadOnlyList<LimitRule> Rules { get; init; } = Array.Empty<LimitRule>();

    /// <summary>按 scope+resource 归并后的规则（Priority 降序）；构造期一次性编译。</summary>
    public IReadOnlyDictionary<(LimitScopeKind Scope, string Resource), IReadOnlyList<LimitRule>> Index { get; init; }
        = new Dictionary<(LimitScopeKind, string), IReadOnlyList<LimitRule>>();

    /// <summary>编译索引（纯函数）：同键规则按 Priority 降序、其次 HardLimit 升序。</summary>
    public static LimitPolicyDocument Create(
        int version,
        LimitUnknownPolicy unknownPolicy,
        long fixedEstimate,
        string? unlimitedAuditReason,
        IReadOnlyDictionary<string, LimitResourceSpec> resources,
        IReadOnlyList<LimitRule> rules)
    {
        var index = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Resource))
            .GroupBy(r => (r.Scope, r.Resource))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LimitRule>)g
                    .OrderByDescending(r => r.Priority)
                    .ThenBy(r => r.HardLimit ?? long.MaxValue)
                    .ToArray());

        return new LimitPolicyDocument
        {
            Version = version,
            UnknownPolicy = unknownPolicy,
            FixedEstimate = fixedEstimate,
            UnlimitedAuditReason = unlimitedAuditReason,
            Resources = resources,
            Rules = rules,
            Index = index,
        };
    }

    /// <summary>取某 scope+resource 的规则（无则空）。</summary>
    public IReadOnlyList<LimitRule> RulesFor(LimitScopeKind scope, string resource)
        => Index.TryGetValue((scope, resource), out var list) ? list : Array.Empty<LimitRule>();

    /// <summary>
    /// 启动校验：必须存在进程级 llm.calls / llm.tokens.input / llm.tokens.output 中至少一条 hard 规则；
    /// 显式 unlimited 时必须带审计理由（L5/G6：禁止裸奔且必须可审）。
    /// </summary>
    public void ValidateForStartup()
    {
        var errors = new List<string>();

        foreach (var r in Rules)
        {
            if (string.IsNullOrWhiteSpace(r.Resource)) errors.Add("规则 resource 不能为空");
            if (r.Window == LimitWindowKind.Window && r.WindowSeconds <= 0)
                errors.Add($"{r.Resource}@{r.Scope} 窗口规则 windowSeconds 必须 > 0");
            if (r.HardLimit is < 0 || r.SoftLimit is < 0)
                errors.Add($"{r.Resource}@{r.Scope} 阈值不能为负数");
            if (r.SoftLimit is { } soft && r.HardLimit is { } hard && soft > hard)
                errors.Add($"{r.Resource}@{r.Scope} soft({soft}) 不能大于 hard({hard})");
            if (!r.HasThreshold)
                errors.Add($"{r.Resource}@{r.Scope} 未声明 hard/soft 阈值（无阈值规则不参与裁决）");
        }

        var hasProcessHard = Rules.Any(r =>
            r.Scope == LimitScopeKind.Process
            && r.HardLimit is > 0
            && r.Resource is ResourceKeys.LlmCalls or ResourceKeys.LlmInputTokens or ResourceKeys.LlmOutputTokens);

        if (!hasProcessHard && string.IsNullOrWhiteSpace(UnlimitedAuditReason))
            errors.Add("缺少进程级 LLM 资源 hard 规则；若确需不限，请显式声明 unlimited=true 且给出 auditReason");

        if (errors.Count > 0)
            throw new InvalidOperationException("limits.json 启动校验失败：" + string.Join("；", errors));
    }

    /// <summary>框架安全兜底默认文档（与既有 GlobalGate 默认 1000 / 5M / 2M / 8 行为等价，供配置缺失时使用）。</summary>
    public static LimitPolicyDocument Default { get; } = Create(
        version: 1,
        unknownPolicy: LimitUnknownPolicy.Conservative,
        fixedEstimate: 0,
        unlimitedAuditReason: null,
        resources: new Dictionary<string, LimitResourceSpec>(StringComparer.Ordinal)
        {
            [ResourceKeys.LlmCalls] = new("count"),
            [ResourceKeys.LlmInputTokens] = new("token"),
            [ResourceKeys.LlmOutputTokens] = new("token"),
            [ResourceKeys.ToolCalls] = new("count"),
            [ResourceKeys.WallMs] = new("ms"),
            [ResourceKeys.Concurrency] = new("gauge"),
        },
        rules: new[]
        {
            new LimitRule(ResourceKeys.LlmCalls, LimitScopeKind.Process, LimitWindowKind.Window, 3600,
                HardLimit: 1000, SoftLimit: null, Action: LimitVerdict.Deny),
            new LimitRule(ResourceKeys.LlmInputTokens, LimitScopeKind.Process, LimitWindowKind.Lifetime, 0,
                HardLimit: 5_000_000, SoftLimit: null, Action: LimitVerdict.Deny),
            new LimitRule(ResourceKeys.LlmOutputTokens, LimitScopeKind.Process, LimitWindowKind.Lifetime, 0,
                HardLimit: 2_000_000, SoftLimit: null, Action: LimitVerdict.Deny),
            new LimitRule(ResourceKeys.Concurrency, LimitScopeKind.Process, LimitWindowKind.Lifetime, 0,
                HardLimit: 8, SoftLimit: null, Action: LimitVerdict.Deny, IsGauge: true),
            new LimitRule(ResourceKeys.LlmInputTokens, LimitScopeKind.Run, LimitWindowKind.Lifetime, 0,
                HardLimit: 400_000, SoftLimit: 200_000, Action: LimitVerdict.Terminate),
            new LimitRule(ResourceKeys.ToolCalls, LimitScopeKind.Round, LimitWindowKind.Lifetime, 0,
                HardLimit: 32, SoftLimit: null, Action: LimitVerdict.Deny),
            new LimitRule(ResourceKeys.WallMs, LimitScopeKind.Run, LimitWindowKind.Lifetime, 0,
                HardLimit: 300_000, SoftLimit: null, Action: LimitVerdict.Terminate),
        });
}

/// <summary>limits.json 读取/反序列化（机制层；不引入新 DSL）。</summary>
public static class LimitPolicyJson
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>解析 JSON 为策略文档（不校验启动判据；调用方按需 <see cref="LimitPolicyDocument.ValidateForStartup"/>）。</summary>
    public static LimitPolicyDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize<PolicyDto>(json, Opt)
                  ?? throw new InvalidOperationException("limits.json 解析为空");
        return Compile(dto);
    }

    /// <summary>读取文件；缺失返回 null（由宿主决定默认文档或失败）。</summary>
    public static LimitPolicyDocument? LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return Load(File.ReadAllText(path));
    }

    private static LimitPolicyDocument Compile(PolicyDto dto)
    {
        var rules = new List<LimitRule>();
        foreach (var r in dto.Rules ?? new List<RuleDto>())
        {
            rules.Add(new LimitRule(
                Resource: r.Resource ?? string.Empty,
                Scope: r.Scope,
                Window: r.Window,
                WindowSeconds: r.WindowSeconds,
                HardLimit: r.Hard,
                SoftLimit: r.Soft,
                Action: r.Action,
                Priority: r.Priority,
                IsGauge: r.IsGauge,
                ThrottleDelayMs: r.ThrottleDelayMs > 0 ? r.ThrottleDelayMs : 1000));
        }

        var resources = new Dictionary<string, LimitResourceSpec>(StringComparer.Ordinal);
        foreach (var (key, spec) in dto.Resources ?? new Dictionary<string, ResourceDto>())
            resources[key] = new LimitResourceSpec(string.IsNullOrWhiteSpace(spec.Unit) ? "count" : spec.Unit!);

        return LimitPolicyDocument.Create(
            dto.Version <= 0 ? 1 : dto.Version,
            dto.UnknownPolicy,
            dto.FixedEstimate,
            dto.Audit is { } a && a.RequireReasonForUnlimited && string.IsNullOrWhiteSpace(dto.UnlimitedAuditReason)
                ? null
                : dto.UnlimitedAuditReason,
            resources,
            rules);
    }

    private sealed class PolicyDto
    {
        public int Version { get; set; } = 1;
        public LimitUnknownPolicy UnknownPolicy { get; set; } = LimitUnknownPolicy.Conservative;
        public long FixedEstimate { get; set; }
        public bool Unlimited { get; set; }
        public string? UnlimitedAuditReason { get; set; }
        public Dictionary<string, ResourceDto>? Resources { get; set; }
        public List<RuleDto>? Rules { get; set; }
        public AuditDto? Audit { get; set; }
    }

    private sealed class ResourceDto
    {
        public string? Unit { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Resource { get; set; }
        public LimitScopeKind Scope { get; set; }
        public LimitWindowKind Window { get; set; }
        public long WindowSeconds { get; set; }
        public long? Hard { get; set; }
        public long? Soft { get; set; }
        public LimitVerdict Action { get; set; } = LimitVerdict.Deny;
        public int Priority { get; set; }
        public bool IsGauge { get; set; }
        public long ThrottleDelayMs { get; set; }
    }

    private sealed class AuditDto
    {
        public bool RequireReasonForUnlimited { get; set; }
    }
}
