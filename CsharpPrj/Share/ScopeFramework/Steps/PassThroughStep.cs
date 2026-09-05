namespace Share.ScopeFramework;

/// <summary>
/// S0 直通：命令不带 scope 时，原样回显原始 JSON 字段（L0 最灵活、向后兼容）。设计 §4.4。
/// </summary>
internal sealed class PassThroughStep : IStep
{
    public string Id => "S0";

    public bool CanExecute(ScopeContext scope) => true;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
        => string.IsNullOrWhiteSpace(scope.RawRequestJson)
            ? Array.Empty<ScopeAction>()
            : new[] { new ScopeAction { Action = "passthrough", RawJson = scope.RawRequestJson! } };
}
