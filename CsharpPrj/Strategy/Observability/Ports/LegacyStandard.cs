// 过渡件（J2 签名面切换用；**J3 随 ③ 绑定声明家族一并删除**，禁新增消费方）。
// 职责：③ 绑定声明（*BindingSpec / *Spec record）→ ① 标准声明（DataCenter.Protocol.Standards，唯一数据源）的反向映射，
// 使旧绑定类在求值助手统一切换到 ① 之后仍可复用同一实现（单一实现点，防迁移期语义分叉）。
// 口径：位序搬运（① ↔ ③ 逐位同名同数，由 SpecPortMirrorGenerationTests.Pairs_ArePositionallyAligned 机检背书），零领域语义。

using DataCenter.Protocol.Standards;
namespace Strategy.Observability.Ports;

/// <summary>
/// ③ 绑定声明 → ① 标准声明（过渡映射，见文件头）。正向映射（① → ③）仍由生成物
/// SpecHost\StandardBinding.g.cs 承担；本件只服务「求值面已切 ①、声明面仍收 ③」的过渡期。
/// </summary>
public static class LegacyStandard
{
    /// <summary>ComposedArgSpec → ComposedArgStandard（生成物）。</summary>
    public static ComposedArgStandard Arg(ComposedArgSpec d) => new(
        d.Convert,
        d.ArgIndex,
        d.Key,
        d.Dep,
        d.Value,
        d.ElementType,
        d.Optional,
        d.Default,
        d.NullLiteral);
    
    /// <summary>ComposedDepSpec → DepStandard（生成物）。</summary>
    public static DepStandard Dep(ComposedDepSpec d) => new(
        d.New,
        d.Factory,
        d.Fake,
        d.Contract,
        d.Wrap,
        d.Methods,
        d.Values,
        d.GenericArgs,
        d.Args?.Select(Arg).ToArray());
    
    /// <summary>ComposedCallSpec → CallStandard（生成物）。</summary>
    public static CallStandard Call(ComposedCallSpec d) => new(
        d.As,
        d.Target,
        d.Method,
        d.IsProperty,
        d.Dep,
        d.Await,
        d.Expect,
        d.Args?.Select(Arg).ToArray());
    
    /// <summary>ComposedFieldSpec → ComposedFieldStandard（生成物）。</summary>
    public static ComposedFieldStandard Field(ComposedFieldSpec d) => new(
        d.Field,
        d.Call,
        d.Select,
        d.Other,
        d.Path,
        d.Dep,
        d.Method,
        d.Key);
    
    /// <summary>ScriptedArgSpec → ValueSourceStandard（生成物）。</summary>
    public static ValueSourceStandard ArgSpec(ScriptedArgSpec d) => new(
        d.From,
        d.Index,
        d.Key,
        d.Dep,
        d.Path,
        d.Convert,
        d.Optional,
        d.Default,
        d.NullLiteral,
        d.Value,
        d.Type,
        d.GenericArgs,
        d.Scope,
        d.Name,
        d.Rows is null ? null : ArgSpec(d.Rows),
        d.Items is null ? null : ArgSpec(d.Items),
        d.RowValue is null ? null : Expr(d.RowValue),
        d.Filter is null ? null : Expr(d.Filter),
        d.Separator,
        d.OnBlankFirstKey,
        d.OnDuplicate,
        d.Distinct,
        d.ElementType,
        d.Construct is null ? null : New(d.Construct),
        d.Named,
        d.Delegate is null ? null : Delegate(d.Delegate));
    
    /// <summary>ScriptedExpr → ExprStandard（生成物）。</summary>
    public static ExprStandard Expr(ScriptedExpr d) => new(
        d.Source is null ? null : ArgSpec(d.Source),
        d.Path,
        d.Count is null ? null : Expr(d.Count),
        d.NotNull is null ? null : Expr(d.NotNull),
        d.AsText is null ? null : Expr(d.AsText),
        d.TypeName is null ? null : Expr(d.TypeName),
        d.Message is null ? null : Expr(d.Message),
        d.Thrown is null ? null : Expr(d.Thrown),
        d.Concat?.Select(Expr).ToArray(),
        d.Separator,
        d.Contains is null ? null : Expr(d.Contains),
        d.Of is null ? null : Expr(d.Of),
        d.All is null ? null : Expr(d.All),
        d.Any is null ? null : Expr(d.Any),
        d.AllIn is null ? null : Expr(d.AllIn),
        d.Call,
        d.CallTarget,
        d.Spread is null ? null : Expr(d.Spread),
        d.Except,
        d.CountEqual?.Select(Expr).ToArray(),
        d.Collect,
        d.CollectText,
        d.SameAsLast,
        d.IsNull is null ? null : Expr(d.IsNull),
        d.Not is null ? null : Expr(d.Not),
        d.Eq?.Select(Expr).ToArray(),
        d.Ne?.Select(Expr).ToArray(),
        d.Gt?.Select(Expr).ToArray(),
        d.Ge?.Select(Expr).ToArray(),
        d.Lt?.Select(Expr).ToArray(),
        d.Le?.Select(Expr).ToArray(),
        d.AllOf?.Select(Expr).ToArray(),
        d.AnyOf?.Select(Expr).ToArray(),
        d.Invoke is null ? null : Invoke(d.Invoke),
        d.First is null ? null : Expr(d.First),
        d.FirstWhere is null ? null : Expr(d.FirstWhere),
        d.FirstPath,
        d.Agg is null ? null : Expr(d.Agg),
        d.AggOp,
        d.AggPath,
        d.Filter is null ? null : Expr(d.Filter),
        d.FilterWhere is null ? null : Expr(d.FilterWhere),
        d.FilterPath,
        d.SameReference?.Select(Expr).ToArray(),
        d.Coalesce?.Select(Expr).ToArray(),
        d.Dict is null ? null : Expr(d.Dict),
        d.DictKey is null ? null : Expr(d.DictKey),
        d.DictValue is null ? null : Expr(d.DictValue),
        d.DictWhere is null ? null : Expr(d.DictWhere),
        d.DictOnDuplicate,
        d.Last is null ? null : Expr(d.Last),
        d.LastWhere is null ? null : Expr(d.LastWhere),
        d.LastPath,
        d.FirstOrNull,
        d.LastOrNull,
        d.Parts?.Select(Part).ToArray(),
        d.Distinct is null ? null : Expr(d.Distinct),
        d.SequenceEqual?.Select(Expr).ToArray(),
        d.Fold is null ? null : Expr(d.Fold),
        d.FoldAs,
        d.FoldInit is null ? null : ArgSpec(d.FoldInit),
        d.FoldStep is null ? null : Chain(d.FoldStep));
    
    /// <summary>ScriptedConcatPartSpec → ConcatPartStandard（生成物）。</summary>
    public static ConcatPartStandard Part(ScriptedConcatPartSpec d) => new(
        Expr(d.Expr),
        d.When is null ? null : Expr(d.When));
    
    /// <summary>ScriptedFieldSpec → FieldExprStandard（生成物）。</summary>
    public static FieldExprStandard FieldSpec(ScriptedFieldSpec d) => new(
        d.Field,
        Expr(d.Expr));
    
    /// <summary>ScriptedStep → StepStandard（生成物）。</summary>
    public static StepStandard Step(ScriptedStep d) => new(
        d.Collects?.Select(Collect).ToArray(),
        d.When is null ? null : Expr(d.When),
        d.New is null ? null : New(d.New),
        d.Invoke is null ? null : Invoke(d.Invoke),
        d.Foreach is null ? null : Foreach(d.Foreach));
    
    /// <summary>ScriptedInvokeSpec → InvokeStandard（生成物）。</summary>
    public static InvokeStandard Invoke(ScriptedInvokeSpec d) => new(
        d.As,
        d.Dep,
        d.Target,
        d.Method,
        d.IsProperty,
        d.Await,
        d.Expect,
        d.Args?.Select(ArgSpec).ToArray(),
        d.Out,
        d.Break,
        d.GenericArgs,
        d.Value is null ? null : ArgSpec(d.Value),
        d.NonPublic);
    
    /// <summary>ScriptedForeachSpec → ForeachStandard（生成物）。</summary>
    public static ForeachStandard Foreach(ScriptedForeachSpec d) => new(
        d.Slot,
        d.Items is null ? null : Expr(d.Items),
        d.As,
        d.Where is null ? null : Expr(d.Where),
        d.Do?.Select(Step).ToArray());
    
    /// <summary>ScriptedNewSpec → NewStandard（生成物）。</summary>
    public static NewStandard New(ScriptedNewSpec d) => new(
        d.As,
        d.Type,
        d.GenericArgs,
        d.Args?.Select(ArgSpec).ToArray(),
        d.Chain?.Select(Chain).ToArray(),
        d.Init?.Select(Init).ToArray());
    
    /// <summary>ScriptedInitSpec → InitStandard（生成物）。</summary>
    public static InitStandard Init(ScriptedInitSpec d) => new(
        d.Member,
        d.Value is null ? null : ArgSpec(d.Value));
    
    /// <summary>ScriptedChainSpec → ChainStandard（生成物）。</summary>
    public static ChainStandard Chain(ScriptedChainSpec d) => new(
        d.Method,
        d.Args?.Select(ArgSpec).ToArray(),
        d.Rebind);
    
    /// <summary>ScriptedDelegateSpec → DelegateStandard（生成物）。</summary>
    public static DelegateStandard Delegate(ScriptedDelegateSpec d) => new(
        d.Kind,
        d.Steps?.Select(Step).ToArray(),
        d.Result is null ? null : Expr(d.Result),
        d.Parameters,
        d.Collect,
        d.Await);
    
    /// <summary>ScriptedCollectSpec → CollectStandard（生成物）。</summary>
    public static CollectStandard Collect(ScriptedCollectSpec d) => new(
        d.Slot,
        d.From);
    
    /// <summary>ScriptedSaveSpec → SaveStandard（生成物）。</summary>
    public static SaveStandard Save(ScriptedSaveSpec d) => new(
        d.Slot,
        d.Type,
        d.Key,
        d.GenericArgs);
    
    /// <summary>ScriptedScopeSpec → ScopeStandard（生成物）。</summary>
    public static ScopeStandard Scope(ScriptedScopeSpec d) => new(
        d.Kind,
        d.As,
        d.Prefix,
        d.Files?.Select(File).ToArray(),
        d.Invoke is null ? null : Invoke(d.Invoke),
        d.Save?.Select(Save).ToArray(),
        d.Open?.Select(Step).ToArray(),
        d.Close?.Select(Step).ToArray());
    
    /// <summary>ScriptedFileSpec → FileStandard（生成物）。</summary>
    public static FileStandard File(ScriptedFileSpec d) => new(
        d.Name,
        d.Dir is null ? null : Expr(d.Dir),
        d.From is null ? null : ArgSpec(d.From),
        d.Content is null ? null : Expr(d.Content),
        d.When is null ? null : Expr(d.When));
    
    /// <summary>ScriptedDeliverSpec → DeliverStandard（生成物）。</summary>
    public static DeliverStandard Deliver(ScriptedDeliverSpec d) => new(
        d.Kind,
        d.Defer,
        d.Issues,
        d.IssueField,
        d.Capture is null ? null : Capture(d.Capture),
        d.DeferSteps?.Select(Step).ToArray(),
        d.Value is null ? null : Expr(d.Value));
    
    /// <summary>ScriptedDeliverCaptureSpec → DeliverCaptureStandard（生成物）。</summary>
    public static DeliverCaptureStandard Capture(ScriptedDeliverCaptureSpec d) => new(
        Invoke(d.Call),
        d.Expect,
        d.Message,
        d.MessageField,
        d.When is null ? null : Expr(d.When));
    /// <summary>
    /// 组合声明（③）→ ① impl 段（过渡映射；供旧绑定类在签名面切换后继续复用 ① 求值助手）。
    /// </summary>
    public static PortImplStandard Impl(ComposedBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return new PortImplStandard(
            PortImplVocabulary.KindName.Composed,
            Deps: spec.Deps.Select(Dep).ToArray(),
            Calls: spec.Calls.Select(Call).ToArray(),
            Project: spec.Project.Select(Field).ToArray());
    }

    /// <summary>脚本声明（③）→ ① impl 段（过渡映射；场景主体按 ScenarioBodyStandard 逐层映射）。</summary>
    public static PortImplStandard Impl(ScriptedBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return new PortImplStandard(
            PortImplVocabulary.KindName.Scripted,
            Binds: Binds(spec.Binds),
            Deps: spec.Deps.Select(Dep).ToArray(),
            Script: spec.Script.Select(Step).ToArray(),
            ScriptProject: spec.Project.Select(FieldSpec).ToArray(),
            Deliver: Deliver(spec.Deliver),
            Scopes: spec.Scopes.Select(Scope).ToArray(),
            Scenarios: spec.Scenarios is null ? null : Scenario(spec.Scenarios));
    }

    /// <summary>场景声明（③）→ ①（场景主体 = ScenarioBodyStandard：不携端口键与工厂表 —— 后者是装配产物）。</summary>
    public static ScenarioStandard Scenario(ScriptedScenarioSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var cases = new Dictionary<string, ScenarioBodyStandard>(StringComparer.Ordinal);
        foreach (var (name, body) in spec.Cases)
        {
            cases[name] = new ScenarioBodyStandard(
                Binds(body.Binds),
                body.Deps.Select(Dep).ToArray(),
                body.Script.Select(Step).ToArray(),
                body.Project.Select(FieldSpec).ToArray(),
                Deliver(body.Deliver),
                body.Scopes.Select(Scope).ToArray());
        }

        return new ScenarioStandard(ArgSpec(spec.Select), cases);
    }

    /// <summary>输入绑定表（③ → ①；槽名为开放映射，键序不参与语义）。</summary>
    private static Dictionary<string, ValueSourceStandard> Binds(
        IReadOnlyDictionary<string, ScriptedArgSpec> binds)
        => binds.ToDictionary(pair => pair.Key, pair => ArgSpec(pair.Value), StringComparer.Ordinal);


}
