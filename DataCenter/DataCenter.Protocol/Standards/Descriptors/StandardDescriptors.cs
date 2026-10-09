namespace DataCenter.Protocol.Standards.Descriptors;

/// <summary>
/// 标准声明描述符表（描述符单源化 §2；唯一手写真源）——把 T1（键白名单）上收为**成员级声明**，
/// 使 T1 / T3 / T4 / T5 均为本表的纯派生。
/// <para>
/// 增量落地：本表分批扩充（<c>StandardDescriptorTests</c> 以 <c>KeysOf</c> 逐层对拍锁定等价）；
/// 未登记的层（<c>step</c> / <c>scope</c> / <c>deliver</c> / <c>valueSource</c> / <c>expr</c> …）随后续批补齐。
/// </para>
/// <para>纪律：本件零机制、零 IO；成员名与承载类型严格同名（反射对拍）。</para>
/// </summary>
public static class StandardDescriptors
{
    /// <summary><c>ports[x].impl</c> 层。注意 <see cref="PortImplStandard.ScriptProject"/> 与 <see cref="PortImplStandard.Project"/> **共用 <c>project</c> 键**（按 kind 二选一），故其 <c>JsonKey = null</c>。</summary>
    public static readonly LayerDescriptor Impl = new(
        "impl",
        "ImplKeys",
        typeof(PortImplStandard),
        [
            new("Kind", "kind", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.Kinds), Required: true),
            new("Id", "id", MemberKind.Scalar),
            new("Target", "target", MemberKind.Scalar),
            new("Method", "method", MemberKind.Scalar),
            new("Reason", "reason", MemberKind.Scalar),
            new("LegacyKind", "legacyKind", MemberKind.Scalar),
            new("ProductFields", "productFields", MemberKind.Scalar),
            new("Args", "args", MemberKind.Children, ChildLayer: "arg", EmptyOnAbsent: true),
            new("Binds", "bind", MemberKind.KeyedMap, ChildLayer: "valueSource", EmptyOnAbsent: true, Gate: "scripted"),
            new("Deps", "deps", MemberKind.Children, ChildLayer: "dep", EmptyOnAbsent: true),
            new("Calls", "calls", MemberKind.Children, ChildLayer: "call", EmptyOnAbsent: true, Gate: "composed"),
            new("Project", "project", MemberKind.Children, ChildLayer: "composedField", EmptyOnAbsent: true, Gate: "composed"),
            new("Script", "script", MemberKind.Children, ChildLayer: "step", EmptyOnAbsent: true, Gate: "scripted"),
            new("ScriptProject", null, MemberKind.Children, ChildLayer: "fieldExpr", Gate: "scripted", Reader: "scriptProject"),
            new("Deliver", "deliver", MemberKind.Child, ChildLayer: "deliver", Gate: "scripted", Reader: "deliver"),
            new("Scopes", "scopes", MemberKind.Children, ChildLayer: "scope", Gate: "scripted", Reader: "scopes"),
            new("Scenarios", "scenarios", MemberKind.Child, ChildLayer: "scenario", Gate: "scripted", Reader: "scenarios"),
        ]);

    /// <summary>反射族位置实参层（<c>impl.args[]</c>）。</summary>
    public static readonly LayerDescriptor Arg = new(
        "arg",
        "ArgKeys",
        typeof(PortArgStandard),
        [
            new("Convert", "convert", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.Conversions), Required: true),
            new("Optional", "optional", MemberKind.Scalar),
            new("NullLiteral", "nullLiteral", MemberKind.Scalar),
            new("Default", "default", MemberKind.Payload),
            new("From", "from", MemberKind.Scalar),
            new("Key", "key", MemberKind.Scalar),
        ]);

    /// <summary>依赖声明层（<c>impl.deps[]</c>）。</summary>
    public static readonly LayerDescriptor Dep = new(
        "dep",
        "DepKeys",
        typeof(DepStandard),
        [
            new("New", "new", MemberKind.Scalar),
            new("Factory", "factory", MemberKind.Scalar),
            new("Fake", "fake", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.FakeKinds)),
            new("Contract", "contract", MemberKind.Scalar),
            new("Wrap", "wrap", MemberKind.Scalar),
            new("Methods", "methods", MemberKind.Scalar),
            new("Values", "values", MemberKind.Payload),
            new("GenericArgs", "genericArgs", MemberKind.Scalar),
            new("Args", "args", MemberKind.Children, ChildLayer: "composedArg"),
            new("Calls", "calls", MemberKind.Children, ChildLayer: "fakeCall"),
            new("Capture", "capture", MemberKind.Child, ChildLayer: "fakeCapture"),
            new("State", "state", MemberKind.Child, ChildLayer: "fakeState"),
        ]);

    /// <summary>有状态替身层（<c>deps[].state</c>；写 / 读成员 + 键实参下标）。</summary>
    public static readonly LayerDescriptor FakeState = new(
        "fakeState",
        "FakeStateKeys",
        typeof(FakeStateStandard),
        [
            new("Write", "write", MemberKind.Scalar, Required: true),
            new("Read", "read", MemberKind.Scalar),
            new("Key", "key", MemberKind.Scalar),
        ]);

    /// <summary>组合族调用声明层（<c>impl.calls[]</c>）；<c>property</c> 键 ↔ <see cref="CallStandard.IsProperty"/>。</summary>
    public static readonly LayerDescriptor Call = new(
        "call",
        "CallKeys",
        typeof(CallStandard),
        [
            new("As", "as", MemberKind.Scalar, Required: true),
            new("Target", "target", MemberKind.Scalar),
            new("Method", "method", MemberKind.Scalar, Required: true),
            new("IsProperty", "property", MemberKind.Scalar),
            new("Dep", "dep", MemberKind.Scalar),
            new("Await", "await", MemberKind.Scalar),
            new("Expect", "expect", MemberKind.Scalar),
            new("Args", "args", MemberKind.Children, ChildLayer: "composedArg"),
        ],
        Reader: "dep");

    /// <summary>组合族投影声明层（<c>impl.project[]</c>）。</summary>
    public static readonly LayerDescriptor ComposedField = new(
        "composedField",
        "ComposedFieldKeys",
        typeof(ComposedFieldStandard),
        [
            new("Field", "field", MemberKind.Scalar, Required: true),
            new("Call", "call", MemberKind.Scalar),
            new("Select", "select", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.Selects), Required: true),
            new("Other", "other", MemberKind.Scalar),
            new("Path", "path", MemberKind.Scalar),
            new("Dep", "dep", MemberKind.Scalar),
            new("Method", "method", MemberKind.Scalar),
            new("Key", "key", MemberKind.Scalar),
        ]);

    /// <summary>组合族实参层（<c>deps[].args</c> / <c>calls[].args</c>）；与反射族（<c>arg</c>）**不同的一套**。</summary>
    public static readonly LayerDescriptor ComposedArg = new(
        "composedArg",
        "ComposedArgKeys",
        typeof(ComposedArgStandard),
        [
            new("Convert", "convert", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.Conversions), Required: true),
            new("ArgIndex", "argIndex", MemberKind.Scalar),
            new("Key", "key", MemberKind.Scalar),
            new("Dep", "dep", MemberKind.Scalar),
            new("Value", "value", MemberKind.Payload),
            new("ElementType", "elementType", MemberKind.Scalar),
            new("Optional", "optional", MemberKind.Scalar),
            new("Default", "default", MemberKind.Payload),
            new("NullLiteral", "nullLiteral", MemberKind.Scalar),
        ]);

    /// <summary>替身应答脚本项层（<c>deps[].calls[]</c>）；<see cref="FakeCallStandard.HasValue"/> 为派生位（无键）。</summary>
    public static readonly LayerDescriptor FakeCall = new(
        "fakeCall",
        "FakeCallKeys",
        typeof(FakeCallStandard),
        [
            new("Member", "member", MemberKind.Scalar),
            new("Position", "position", MemberKind.Scalar),
            new("Value", "value", MemberKind.Payload),
            new("HasValue", null, MemberKind.Scalar, Reader: "fakeCallHasValue"),
            new("Throw", "throw", MemberKind.Scalar),
            new("Message", "message", MemberKind.Scalar),
        ]);

    /// <summary>替身调用捕获层（<c>deps[].capture</c>）。</summary>
    public static readonly LayerDescriptor FakeCapture = new(
        "fakeCapture",
        "FakeCaptureKeys",
        typeof(FakeCaptureStandard),
        [
            new("Member", "member", MemberKind.Scalar),
        ]);

    /// <summary>主动抛出步骤层（<c>script[].throw</c>）。</summary>
    public static readonly LayerDescriptor Throw = new(
        "throw",
        "ThrowKeys",
        typeof(ThrowStandard),
        [
            new("Type", "type", MemberKind.Scalar, Required: true),
            new("Message", "message", MemberKind.Scalar),
        ]);

    /// <summary>收集声明层（<c>script[].collect</c>；裸字符串 = 槽名简写）。</summary>
    public static readonly LayerDescriptor Collect = new(
        "collect",
        "CollectKeys",
        typeof(CollectStandard),
        [
            new("Slot", "slot", MemberKind.Scalar, Required: true),
            new("From", "from", MemberKind.Scalar),
        ]);

    /// <summary>链式后置调用层（<c>new.chain[]</c>）。</summary>
    public static readonly LayerDescriptor Chain = new(
        "chain",
        "ChainKeys",
        typeof(ChainStandard),
        [
            new("Method", "method", MemberKind.Scalar, Required: true),
            new("Args", "args", MemberKind.Children, ChildLayer: "valueSource"),
            new("Rebind", "rebind", MemberKind.Scalar),
        ]);

    /// <summary>属性 / 字段初始化层（<c>new.init[]</c>）。</summary>
    public static readonly LayerDescriptor Init = new(
        "init",
        "InitKeys",
        typeof(InitStandard),
        [
            new("Member", "member", MemberKind.Scalar, Required: true),
            new("Value", "value", MemberKind.Child, ChildLayer: "valueSource"),
            new("Key", "key", MemberKind.Child, ChildLayer: "valueSource"),
        ]);

    /// <summary>进入期留存层（<c>scope.restore.save[]</c>）。</summary>
    public static readonly LayerDescriptor Save = new(
        "save",
        "SaveKeys",
        typeof(SaveStandard),
        [
            new("Slot", "slot", MemberKind.Scalar, Required: true),
            new("Type", "type", MemberKind.Scalar, Required: true),
            new("Key", "key", MemberKind.Scalar, Required: true),
            new("GenericArgs", "genericArgs", MemberKind.Scalar),
            new("Set", "set", MemberKind.Opaque, ChildLayer: "valueSource"),
            new("Restore", "restore", MemberKind.Scalar),
        ]);

    /// <summary>拼接段层（<c>concat[]</c>）。</summary>
    public static readonly LayerDescriptor ConcatPart = new(
        "concatPart",
        "ConcatPartKeys",
        typeof(ConcatPartStandard),
        [
            new("Expr", "expr", MemberKind.Child, ChildLayer: "expr", Required: true),
            new("When", "when", MemberKind.Child, ChildLayer: "expr"),
        ]);

    /// <summary>脚本投影项层（<c>impl.project[]</c>；脚本族）。</summary>
    public static readonly LayerDescriptor FieldExpr = new(
        "fieldExpr",
        "FieldExprKeys",
        typeof(FieldExprStandard),
        [
            new("Field", "field", MemberKind.Scalar, Required: true),
            new("Expr", "expr", MemberKind.Child, ChildLayer: "expr", Required: true),
        ]);

    /// <summary>作用域声明层（<c>impl.scopes[]</c>）。</summary>
    public static readonly LayerDescriptor Scope = new(
        "scope",
        "ScopeKeys",
        typeof(ScopeStandard),
        [
            new("Kind", "kind", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.ScopeKinds), Required: true),
            new("As", "as", MemberKind.Scalar, Required: true),
            new("Prefix", "prefix", MemberKind.Scalar),
            new("Files", "files", MemberKind.Children, ChildLayer: "file"),
            new("Invoke", "invoke", MemberKind.Child, ChildLayer: "invoke"),
            new("Save", "save", MemberKind.Children, ChildLayer: "save"),
            new("Open", "open", MemberKind.Children, ChildLayer: "step"),
            new("Close", "close", MemberKind.Children, ChildLayer: "step"),
        ]);

    /// <summary>载体声明层（<c>impl.deliver</c>）；<see cref="DeliverStandard.DeferSteps"/> 为派生位（无键）。</summary>
    public static readonly LayerDescriptor Deliver = new(
        "deliver",
        "DeliverKeys",
        typeof(DeliverStandard),
        [
            new("Kind", "kind", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.DeliverKinds)),
            new("Defer", "defer", MemberKind.Opaque),
            new("Issues", "issues", MemberKind.Scalar),
            new("IssueField", "issueField", MemberKind.Scalar),
            new("Capture", "capture", MemberKind.Child, ChildLayer: "capture"),
            new("DeferSteps", null, MemberKind.Children, ChildLayer: "step"),
            new("Value", "value", MemberKind.Child, ChildLayer: "expr"),
        ]);

    /// <summary>场景表声明层（<c>impl.scenarios</c>）；<c>cases</c> 为开放映射。</summary>
    public static readonly LayerDescriptor Scenario = new(
        "scenario",
        "ScenarioKeys",
        typeof(ScenarioStandard),
        [
            new("Select", "select", MemberKind.Child, ChildLayer: "valueSource", Required: true),
            new("Cases", "cases", MemberKind.KeyedMap, ChildLayer: "scenarioBody", Required: true),
        ]);

    /// <summary>构造步骤层（<c>script[].new</c>）。</summary>
    public static readonly LayerDescriptor New = new(
        "new",
        "NewKeys",
        typeof(NewStandard),
        [
            new("As", "as", MemberKind.Scalar, Required: true),
            new("Type", "type", MemberKind.Scalar, Required: true),
            new("GenericArgs", "genericArgs", MemberKind.Scalar),
            new("Args", "args", MemberKind.Children, ChildLayer: "valueSource"),
            new("Chain", "chain", MemberKind.Children, ChildLayer: "chain"),
            new("Init", "init", MemberKind.Children, ChildLayer: "init"),
            new("Expect", "expect", MemberKind.Scalar),
        ]);

    /// <summary>委托工厂层（<c>from:delegate</c>）。</summary>
    public static readonly LayerDescriptor Delegate = new(
        "delegate",
        "DelegateKeys",
        typeof(DelegateStandard),
        [
            new("Kind", "kind", MemberKind.Scalar),
            new("Steps", "steps", MemberKind.Children, ChildLayer: "step"),
            new("Result", "result", MemberKind.Child, ChildLayer: "expr"),
            new("Parameters", "parameters", MemberKind.Scalar),
            new("Collect", "collect", MemberKind.Scalar),
            new("Await", "await", MemberKind.Scalar),
        ]);

    /// <summary>调用步骤层（<c>script[].invoke</c>）；<c>on</c> 键 ↔ <see cref="InvokeStandard.Value"/>（对表达式值调用）。</summary>
    public static readonly LayerDescriptor Invoke = new(
        "invoke",
        "InvokeKeys",
        typeof(InvokeStandard),
        [
            new("As", "as", MemberKind.Scalar),
            new("Dep", "dep", MemberKind.Scalar),
            new("Target", "target", MemberKind.Scalar),
            new("Method", "method", MemberKind.Scalar, Required: true),
            new("IsProperty", "property", MemberKind.Scalar),
            new("Await", "await", MemberKind.Scalar),
            new("Expect", "expect", MemberKind.Scalar),
            new("Args", "args", MemberKind.Children, ChildLayer: "valueSource"),
            new("Out", "out", MemberKind.Scalar),
            new("Break", "break", MemberKind.Scalar),
            new("GenericArgs", "genericArgs", MemberKind.Scalar),
            new("Value", "on", MemberKind.Child, ChildLayer: "valueSource"),
            new("NonPublic", "nonPublic", MemberKind.Scalar),
            new("KeepTask", "keepTask", MemberKind.Scalar),
        ]);

    /// <summary>
    /// 取值来源层（<c>bind</c> / 步骤实参 / 表达式源共用；对应内核 <c>ScriptedArgSpec</c>）。
    /// <para>多形态（<see cref="LayerDescriptor.Ordered"/>=false）：显式 <c>from</c> 或**按键简写**（<c>arg</c>/<c>with</c>/<c>var</c>/<c>slot</c>/<c>item</c>/<c>literal</c>/<c>table</c>/<c>presence</c>/<c>list</c>/<c>map</c>），
    /// 故多个键映射到同一成员（<see cref="MemberDescriptor.AliasKeys"/>）。</para>
    /// </summary>
    public static readonly LayerDescriptor ValueSource = new(
        "valueSource",
        "ValueSourceKeys",
        typeof(ValueSourceStandard),
        [
            new("From", "from", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.ValueSourceFromAll)),
            new("Index", "index", MemberKind.Scalar, AliasKeys: ["arg"]),
            new("Key", "key", MemberKind.Scalar, AliasKeys: ["with", "var", "slot", "item"]),
            new("Dep", "dep", MemberKind.Scalar),
            new("Value", "value", MemberKind.Payload, AliasKeys: ["literal"]),
            new("Rows", "table", MemberKind.Child, ChildLayer: "valueSource", AliasKeys: ["rows"]),
            new("Items", "presence", MemberKind.Child, ChildLayer: "valueSource", AliasKeys: ["items"]),
            new("ListItems", "list", MemberKind.Children, ChildLayer: "valueSource"),
            new("Pairs", "map", MemberKind.Children, ChildLayer: "valueSource", AliasKeys: ["pairs"]),
            new("Path", "path", MemberKind.Scalar),
            new("Convert", "convert", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.Conversions)),
            new("Optional", "optional", MemberKind.Scalar),
            new("Default", "default", MemberKind.Payload),
            new("NullLiteral", "nullLiteral", MemberKind.Scalar),
            new("Type", "type", MemberKind.Scalar),
            new("GenericArgs", "genericArgs", MemberKind.Scalar),
            new("Scope", "scope", MemberKind.Scalar),
            new("Name", "name", MemberKind.Scalar),
            new("RowValue", "rowValue", MemberKind.Child, ChildLayer: "expr"),
            new("Filter", "where", MemberKind.Child, ChildLayer: "expr"),
            new("Separator", "separator", MemberKind.Scalar),
            new("OnBlankFirstKey", "onBlankFirstKey", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.OnBlankFirstKey)),
            new("OnDuplicate", "onDuplicate", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.OnDuplicate)),
            new("Distinct", "distinct", MemberKind.Scalar),
            new("ElementType", "elementType", MemberKind.Scalar),
            new("Construct", "construct", MemberKind.Child, ChildLayer: "new", AliasKeys: ["new"]),
            new("Named", "named", MemberKind.Scalar),
            new("Delegate", "delegate", MemberKind.Child, ChildLayer: "delegate"),
            new("Json", "json", MemberKind.Scalar),
            new("Content", "content", MemberKind.Scalar),
        ],
        Ordered: false,
        Reader: "valueSource");

    /// <summary>
    /// 脚本步骤层（<c>impl.script[]</c>）——**扁平多模型**：五形态键（new/invoke/foreach/throw/expr）属 <see cref="StepStandard"/>，
    /// <c>items</c>/<c>where</c>/<c>do</c> 属 <see cref="ForeachStandard"/>，值源步骤键（<c>from</c>/<c>pairs</c>/<c>elementType</c>/<c>genericArgs</c>）属 <see cref="ValueSourceStandard"/>。
    /// </summary>
    public static readonly LayerDescriptor Step = new(
        "step",
        "StepKeys",
        typeof(StepStandard),
        [
            new("Collects", "collect", MemberKind.Children, ChildLayer: "collect"),
            new("When", "when", MemberKind.Child, ChildLayer: "expr"),
            new("Foreach", "foreach", MemberKind.Child, ChildLayer: "foreach"),
            new("Items", "items", MemberKind.Child, ChildLayer: "expr", Owner: typeof(ForeachStandard)),
            new("As", "as", MemberKind.Scalar),
            new("Where", "where", MemberKind.Child, ChildLayer: "expr", Owner: typeof(ForeachStandard)),
            new("Do", "do", MemberKind.Children, ChildLayer: "step", Owner: typeof(ForeachStandard)),
            new("New", "new", MemberKind.Child, ChildLayer: "new"),
            new("Invoke", "invoke", MemberKind.Child, ChildLayer: "invoke"),
            new("Throw", "throw", MemberKind.Child, ChildLayer: "throw"),
            new("Expr", "expr", MemberKind.Child, ChildLayer: "expr"),
            new("Source", "from", MemberKind.Child, ChildLayer: "valueSource"),
            new("Pairs", "pairs", MemberKind.Children, ChildLayer: "valueSource", Owner: typeof(ValueSourceStandard)),
            new("ElementType", "elementType", MemberKind.Scalar, Owner: typeof(ValueSourceStandard)),
            new("GenericArgs", "genericArgs", MemberKind.Scalar, Owner: typeof(ValueSourceStandard)),
        ],
        Ordered: false,
        Reader: "step");

    /// <summary>
    /// issue 捕获层（<c>deliver.capture</c>）——**扁平多模型**：<c>call</c>/<c>expect</c>/<c>message</c>/<c>messageField</c>/<c>when</c> 属
    /// <see cref="DeliverCaptureStandard"/>，调用体键（<c>as</c>/<c>dep</c>/<c>target</c>/<c>method</c>/<c>await</c>/<c>args</c>/<c>out</c>）属嵌套 <see cref="InvokeStandard"/>。
    /// </summary>
    public static readonly LayerDescriptor Capture = new(
        "capture",
        "CaptureKeys",
        typeof(DeliverCaptureStandard),
        [
            new("Call", "call", MemberKind.Child, ChildLayer: "invoke"),
            new("As", "as", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Dep", "dep", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Target", "target", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Method", "method", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Await", "await", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Args", "args", MemberKind.Children, ChildLayer: "valueSource", Owner: typeof(InvokeStandard)),
            new("Out", "out", MemberKind.Scalar, Owner: typeof(InvokeStandard)),
            new("Expect", "expect", MemberKind.Scalar),
            new("Message", "message", MemberKind.Enum, Vocabulary: nameof(PortImplVocabulary.MessageModes)),
            new("MessageField", "messageField", MemberKind.Scalar),
            new("When", "when", MemberKind.Child, ChildLayer: "expr"),
        ],
        Ordered: false);

    /// <summary>作用域预置文件层（<c>scope.files[]</c>）；<see cref="FileStandard.NameExpr"/> 为批量形态位（无独立键）。</summary>
    public static readonly LayerDescriptor File = new(
        "file",
        "FileKeys",
        typeof(FileStandard),
        [
            new("Name", "name", MemberKind.Scalar),
            new("Dir", "dir", MemberKind.Child, ChildLayer: "expr"),
            new("From", "from", MemberKind.Child, ChildLayer: "valueSource"),
            new("Content", "content", MemberKind.Child, ChildLayer: "expr"),
            new("When", "when", MemberKind.Child, ChildLayer: "expr"),
            new("Batch", "batch", MemberKind.Child, ChildLayer: "valueSource"),
            new("As", "as", MemberKind.Scalar),
            new("NameExpr", null, MemberKind.Child, ChildLayer: "expr"),
        ]);

    /// <summary>
    /// 表达式层（<c>expr</c>）：键 = 值源键 ∪ 算子键（扁平多形态，键序无语义）；成员**由
    /// <see cref="PortImplVocabulary.ExprSlots"/> 派生**（成员名 / 算子组 / 子槽形态 / 键 / 嵌套键单源），
    /// 描述符表不另抄一份。
    /// <para>
    /// <c>Source</c> 成员一并声明值源键（<see cref="MemberDescriptor.AliasKeys"/>）：<c>expr</c> 对象的取值来源键
    /// 与来源侧**同形**（扁平书写），故其键集并入本层。
    /// </para>
    /// <para>读取仍由 <c>expr</c> 层钩子（<c>StandardJson.ReadExpr</c>）承担；本描述符用途 = 键集单源（结构门 / 完备性）
    /// 与后续遍历侧（批 C ①）的算子组取值域（<see cref="MemberDescriptor.OperatorGroup"/>）。</para>
    /// </summary>
    public static readonly LayerDescriptor Expr = new(
        "expr",
        "ExprKeys",
        typeof(ExprStandard),
        ExprMembers(),
        Ordered: false,
        Reader: "expr");

    /// <summary>由 <see cref="PortImplVocabulary.ExprSlots"/> 派生 <c>expr</c> 层成员（成员序 = 槽表序）。</summary>
    private static IReadOnlyList<MemberDescriptor> ExprMembers()
        => PortImplVocabulary.ExprSlots
            .Select(slot => new MemberDescriptor(
                slot.Member,
                slot.Key,
                ExprMemberKind(slot.Kind, slot.Vocabulary),
                Vocabulary: slot.Vocabulary,
                ChildLayer: ExprChildLayer(slot.Kind),
                NestedKeys: slot.NestedKeys,
                OperatorGroup: slot.OperatorGroup,
                AliasKeys: string.Equals(slot.Member, nameof(ExprStandard.Source), StringComparison.Ordinal)
                    ? StandardDescriptors.ValueSource.Keys
                    : null))
            .ToArray();

    /// <summary>子槽形态 → 成员形态（<see cref="MemberKind"/>；登记词表者 = <see cref="MemberKind.Enum"/>）。</summary>
    private static MemberKind ExprMemberKind(ExprSlotKind kind, string? vocabulary) => kind switch
    {
        ExprSlotKind.None => vocabulary is null ? MemberKind.Scalar : MemberKind.Enum,
        ExprSlotKind.One => MemberKind.Child,
        ExprSlotKind.Many => MemberKind.Children,
        ExprSlotKind.Parts => MemberKind.Children,
        ExprSlotKind.Source => MemberKind.Child,
        ExprSlotKind.Invoke => MemberKind.Child,
        ExprSlotKind.Chain => MemberKind.Child,
        _ => MemberKind.Scalar,
    };

    /// <summary>子槽形态 → 子层名（<c>expr</c> 自递归 / <c>concatPart</c> / <c>valueSource</c> / <c>invoke</c> / <c>chain</c>）。</summary>
    private static string? ExprChildLayer(ExprSlotKind kind) => kind switch
    {
        ExprSlotKind.One or ExprSlotKind.Many => "expr",
        ExprSlotKind.Parts => "concatPart",
        ExprSlotKind.Source => "valueSource",
        ExprSlotKind.Invoke => "invoke",
        ExprSlotKind.Chain => "chain",
        _ => null,
    };

    /// <summary>已登记的全部层（声明序）。覆盖 T1 全键表（含 <c>expr</c>：键 = 值源键 ∪ 算子键，成员由 <c>ExprSlots</c> 派生）。</summary>
    public static readonly IReadOnlyList<LayerDescriptor> All =
        [Impl, Arg, Dep, Call, ComposedField, ComposedArg, FakeCall, FakeCapture, FakeState,
         Throw, Collect, Chain, Init, Save, ConcatPart, FieldExpr,
         Scope, Deliver, Scenario, New, Delegate, Invoke,
         ValueSource, Step, Capture, File, Expr];
}

