using System.Text.Json;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口 <c>impl</c> 段的完整中立形态（标准框架通用化重构 §2.2 第 1 项）：
/// 此前 <see cref="PortStandard.Impl"/> 只存 RawText 字符串，标准层对「怎么装配」零表达力；
/// 本模型把内核 <c>PortLedgerEntry</c> 的全部声明面（实现归属 + 取值 + 组合 + 脚本 + 载体 + 作用域 + 场景）
/// 上收为标准层可独立回答的结构。
/// <para>
/// 分工：本模型只描述「JSON 里写了什么」；「怎么变成可调用边界」仍是内核编译面的职责（本层不引内核类型）。
/// 词表取值域见 <see cref="PortImplVocabulary"/>。
/// </para>
/// </summary>
/// <param name="Kind">绑定方式（**必填**：<c>reflective</c>｜<c>composed</c>｜<c>scripted</c>｜<c>frozen</c>；SK-28 起缺省已取消，遗留值仅 <c>delegate</c> 读兼容，需注入实现声明 <c>impl.seam</c>）。</param>
/// <param name="Id">委派实现的登记 id（仅遗留 <c>kind=delegate</c> 用）。</param>
/// <param name="Target">目标类型全名（反射族）。</param>
/// <param name="Method">目标方法名（反射族）。</param>
/// <param name="Reason">冻结 / 未绑定原因（<c>kind=frozen</c>）。</param>
/// <param name="LegacyKind">spec 面入口名（历史 kind；只读驱动元数据）。</param>
/// <param name="ProductFields">产物字段声明（<c>|</c> 分隔；只读驱动元数据）。</param>
/// <param name="Args">位置实参声明（位序即语义）。</param>
/// <param name="Binds">脚本输入绑定（槽名 → 取值来源；槽名为不透明名，故为开放映射）。</param>
/// <param name="Deps">依赖声明（声明序 = 构建序）。</param>
/// <param name="Calls">调用声明（声明序 = 调用序；组合族）。</param>
/// <param name="Project">组合投影声明（产物字段 ← 调用 / 替身取法）。</param>
/// <param name="Script">脚本步骤声明（声明序 = 执行序）。</param>
/// <param name="ScriptProject">脚本投影声明。</param>
/// <param name="Deliver">载体声明（value / issues / action）。</param>
/// <param name="Scopes">作用域声明（tempDir / using / restore）。</param>
/// <param name="Scenarios">多场景探针声明（声明了才非空）。</param>
/// <param name="Seam">实现注入声明（F2-d 一期；非空 = 本端口由宿主/测试侧 seam provider 提供实现）。</param>
public sealed record PortImplStandard(
    string Kind,
    string? Id = null,
    string? Target = null,
    string? Method = null,
    string? Reason = null,
    string? LegacyKind = null,
    string ProductFields = "",
    IReadOnlyList<PortArgStandard>? Args = null,
    IReadOnlyDictionary<string, ValueSourceStandard>? Binds = null,
    IReadOnlyList<DepStandard>? Deps = null,
    IReadOnlyList<CallStandard>? Calls = null,
    IReadOnlyList<ComposedFieldStandard>? Project = null,
    IReadOnlyList<StepStandard>? Script = null,
    IReadOnlyList<FieldExprStandard>? ScriptProject = null,
    DeliverStandard? Deliver = null,
    IReadOnlyList<ScopeStandard>? Scopes = null,
    ScenarioStandard? Scenarios = null,
    PortSeamStandard? Seam = null);

/// <summary>
/// 端口实参声明（标准框架通用化重构 §2.2 第 2 项）：**实参声明的唯一单源** ——
/// 内核求值链直接消费本类型（<c>PortBindingSpec</c> + <c>PortArgStandardExtensions</c>），不再另立同形副本）：
/// 位序即语义，取值口径由 <see cref="Convert"/> + <see cref="From"/>/<see cref="Key"/> 表达。
/// </summary>
/// <param name="Convert">转换规则（取值域见 <see cref="PortImplVocabulary.Conversions"/>）。</param>
/// <param name="Optional">可省略（缺参 → <see cref="Default"/> 或目标类型默认值）。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
/// <param name="Default">缺省值（可选槽位缺参时使用）。</param>
/// <param name="From">结构化取值来源（仅 <c>json</c> 用：<c>with</c> = 具名实参）。</param>
/// <param name="Key">结构化取值键名（仅 <c>json</c> 用）。</param>
public sealed record PortArgStandard(
    string Convert,
    bool Optional = false,
    bool NullLiteral = false,
    JsonElement? Default = null,
    string? From = null,
    string? Key = null);
