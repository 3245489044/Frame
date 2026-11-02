using System.Runtime.CompilerServices;
using Plate.DslMethod;
using Plate.DslMethod.Parsing;

namespace Usharp.Syntax;

/// <summary>
/// Usharp 前缀作用域（领域包自持注册）：核心结构前缀 13 条（<see cref="UsharpCorePrefixes"/>）+
/// 业务领域前缀 12 条（本类 <see cref="DomainPrefixes"/>，B2 起按域下沉到 <c>Usharp.Packs.*</c>）。
/// <para>
/// 前缀由本包自持（<b>不写入框架核心表</b> <c>DslConstants.Prefixes</c>），并注册到
/// <see cref="DslPrefixRegistry"/>（id = <see cref="PackId"/>）。编译期作用域由
/// <c>UsharpPrefixScope.Resolve(已装载包)</c> 逐次构造（核心 ∪ 已装载包），本类 <see cref="Scope"/>
/// 保留为**全量兼容别名**（测试 / 迁移期 / 编辑器前缀校验共用）。
/// </para>
/// <para>
/// 作用域 = <b>框架核心前缀 ∪ 本包前缀</b>：Usharp 文档仍可使用核心词汇（如 UniversalReference 的
/// <c>[Node]</c>/<c>[Edge]</c>）；「互不可见」针对的是各<b>领域包</b>之间的私有前缀。
/// </para>
/// </summary>
public static class UsharpPrefixes
{
    /// <summary>包标识（<see cref="DslPrefixRegistry"/> 注册键）。</summary>
    public const string PackId = "usharp";

    /// <summary>业务领域前缀（12 条）：B2 起按域下沉到各 <c>Usharp.Packs.*</c>，本类只保留声明序真源至迁移完成。</summary>
    public static readonly IReadOnlyList<DslPrefixDef> DomainPrefixes = new DslPrefixDef[]
    {
        // 数据层语言单元（数据即声明）：表结构 / 记录载荷
        new(UsharpLanguage.TablePrefix,  DslSymbolKind.Table,  "Table"),
        new(UsharpLanguage.RecordPrefix, DslSymbolKind.Record, "Record"),
        // 配置/规则语言单元（L5 配置面）：配置项 / 规则（条件→动作）
        new(UsharpLanguage.ConfigPrefix, DslSymbolKind.Config, "Config"),
        new(UsharpLanguage.RulePrefix,   DslSymbolKind.Rule,   "Rule"),
        // 成员级声明语言单元（[Const]）：C# 类型成员（名+值+类型身份）→ 生成 partial 成员
        new(UsharpLanguage.ConstPrefix,  DslSymbolKind.Const,  "Const"),
        // LLM 判定契约语言单元（[LLMDecl]）：判定形状与契约外置（判定点 when + prompt/输入/输出 schema）
        new(UsharpLanguage.LlmDeclPrefix, DslSymbolKind.LlmDecl, "LLMDecl"),
        // 键空间声明语言单元（[KeySpace]）：键族/面/轴/端口/策略/回退链的声明面
        new(UsharpLanguage.KeySpacePrefix, DslSymbolKind.KeySpace, "KeySpace"),
        // 连接点命令声明语言单元（[Command]）：action/op 路由 + 键配方
        new(UsharpLanguage.CommandPrefix, DslSymbolKind.Command, "Command"),
        // 读模型声明语言单元（[ReadModel]）：模型 id → 源键/版本源/投影器/参数名表
        new(UsharpLanguage.ReadModelPrefix, DslSymbolKind.ReadModel, "ReadModel"),
        // 关系声明语言单元（[Relation]/[Invariant]/[Derive]）：关系操作 / 关系不变量 / 派生成员集
        new(UsharpLanguage.RelationPrefix,  DslSymbolKind.Relation,  "Relation"),
        new(UsharpLanguage.InvariantPrefix, DslSymbolKind.Invariant, "Invariant"),
        new(UsharpLanguage.DerivePrefix,    DslSymbolKind.Derive,    "Derive"),
        // 挂载绑定语言单元（[Mount]）：MountId → relation + trigger/stage/family/order/posture/projection
        new(UsharpLanguage.MountPrefix,     DslSymbolKind.Mount,     "Mount"),
        // 键身份语义层（[Entity]/[KeyLexicon]/[AnswerShape]/[WindowShape]/[KeyPolicy]/[KeyBridge]）
        new(UsharpLanguage.EntityPrefix,      DslSymbolKind.Entity,      "Entity"),
        new(UsharpLanguage.KeyLexiconPrefix,  DslSymbolKind.KeyLexicon,  "KeyLexicon"),
        new(UsharpLanguage.AnswerShapePrefix, DslSymbolKind.AnswerShape, "AnswerShape"),
        new(UsharpLanguage.WindowShapePrefix, DslSymbolKind.WindowShape, "WindowShape"),
        new(UsharpLanguage.KeyPolicyPrefix,   DslSymbolKind.KeyPolicy,   "KeyPolicy"),
        new(UsharpLanguage.KeyBridgePrefix,   DslSymbolKind.KeyBridge,   "KeyBridge"),
    };

    /// <summary>本包全部前缀（核心结构 13 ∪ 业务领域 12）—— <c>usharp</c> 全量语法面。</summary>
    public static readonly IReadOnlyList<DslPrefixDef> OwnPrefixes =
        UsharpCorePrefixes.Definitions.Concat(DomainPrefixes).ToArray();

    /// <summary>Usharp 前缀作用域（**全量兼容别名**）= 框架核心前缀 ∪ 本包全部前缀。</summary>
    public static readonly DslPrefixSet Scope =
        new DslPrefixSet(PackId, DslConstants.Prefixes.Concat(OwnPrefixes));

    /// <summary>把本包作用域登记进注册中心（幂等；本程序集加载时自动执行）。</summary>
    [ModuleInitializer]
    internal static void Register() => DslPrefixRegistry.Register(Scope);
}
