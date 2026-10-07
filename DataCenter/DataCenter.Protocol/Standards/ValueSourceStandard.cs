using System.Text.Json;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 脚本取值来源（标准框架通用化重构 §2.2 第 3 项；对应内核 <c>ScriptedArgSpec</c>）：
/// bind 槽 / 步骤实参 / 表达式源共用同一口径——<c>from</c> 显式或按键简写（<c>arg</c>/<c>with</c>/<c>var</c>/
/// <c>item</c>/<c>literal</c>/<c>table</c>/<c>presence</c>）推断；位序即语义。
/// <para>载荷位（<see cref="Default"/>/<see cref="Value"/>）为原样 JSON：标准层不解释其内部结构。</para>
/// </summary>
/// <param name="From">来源种类（取值域见 <see cref="PortImplVocabulary.ValueSourceFrom"/>）。</param>
/// <param name="Index">位置实参下标（<c>arg</c>）或元素下标（<c>item</c>）。</param>
/// <param name="Key">具名实参键（<c>with</c>）/ 槽名（<c>var</c>）/ 遍历变量名（<c>item</c>）。</param>
/// <param name="Dep">依赖下标（<c>dep</c>）。</param>
/// <param name="Path">属性路径（<c>item</c> / <c>var</c> 取元素或槽对象的属性）。**辨异**：仅 <c>from:file</c> 时 = 仓库相对文件路径（非属性路径）。</param>
/// <param name="Convert">转换规则（取值域见 <see cref="PortImplVocabulary.Conversions"/>）。</param>
/// <param name="Optional">可省略（无值 → <see cref="Default"/> 或目标类型默认值）。</param>
/// <param name="Default">缺省值 JSON（载荷位）。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
/// <param name="Value">声明常量 JSON（<c>literal</c>；载荷位）。</param>
/// <param name="Type">目标类型全名（缺省 = 原义保留）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名（<see cref="Type"/> 为开放泛型时按序闭合）。</param>
/// <param name="Scope">临时目录作用域名（仅 <c>tmpPath</c> / <c>tmpDir</c> 用，对应 <c>scopes[].as</c>）。</param>
/// <param name="Name">作用域内文件相对名（仅 <c>tmpPath</c> 用）。</param>
/// <param name="Rows">行集来源（仅 <c>table</c>）。</param>
/// <param name="Items">词元集来源（仅 <c>presence</c>；缺省 = 复用 <see cref="Rows"/>）。</param>
/// <param name="RowValue">行值表达式（<c>table</c> 用；体内 <c>from:row</c> 引当前行单元格）。</param>
/// <param name="Filter">行 / 词元过滤表达式（<c>table</c> / <c>presence</c> 用）。</param>
/// <param name="Separator">主键列分隔符（<c>table</c>；缺省 <c>|</c>）。</param>
/// <param name="OnBlankFirstKey">首列为空的处置（<c>table</c>；取值域见 <see cref="PortImplVocabulary.OnBlankFirstKey"/>）。</param>
/// <param name="OnDuplicate">键重复的处置（<c>table</c>；取值域见 <see cref="PortImplVocabulary.OnDuplicate"/>）。</param>
/// <param name="Distinct">词元去重（<c>presence</c> 用）。</param>
/// <param name="ElementType">集合元素类型全名（<c>convert:array</c> 且目标类型未给出元素类型时用）。</param>
/// <param name="Construct">内嵌构造声明（本实参的值 = 构造结果）。</param>
/// <param name="Named">命名实参（按形参名绑定而非位置）。</param>
/// <param name="Delegate">委托工厂声明（<c>from:delegate</c> 时按本声明构造委托）。</param>
/// <param name="Json">仅 <c>from:file</c> 用：按 JSON 解析文件内容（等价 <c>content:"json"</c>；与 <c>content</c> 矛盾即抛）；
/// 对象形（<c>{ text, options }</c>）见 <see cref="JsonText"/> / <see cref="JsonOptions"/>（C3）。</param>
/// <param name="Content">仅 <c>from:file</c> 用：内容形态（<c>text</c> 缺省 / <c>lines</c> / <c>json</c>）。</param>
/// <param name="ListItems">列表字面量元素来源（仅 <c>from:list</c>；声明序 = 元素序）。</param>
/// <param name="Pairs">字典字面量键值对（仅 <c>from:map</c>；声明序 = 写入序）。</param>
/// <param name="JsonText">仅 <c>json</c> 对象形用：JSON 文本值源（C3；配合 <see cref="JsonOptions"/> 与 <c>type</c> 反序列化）。</param>
/// <param name="JsonOptions">仅 <c>json</c> 对象形用：<see cref="JsonSerializerOptions"/> 值源（C3；求值结果非该类型即抛）。</param>
public sealed record ValueSourceStandard(
    string? From = null,
    int? Index = null,
    string? Key = null,
    int? Dep = null,
    string? Path = null,
    string? Convert = null,
    bool Optional = false,
    JsonElement? Default = null,
    bool NullLiteral = false,
    JsonElement? Value = null,
    string? Type = null,
    IReadOnlyList<string>? GenericArgs = null,
    string? Scope = null,
    string? Name = null,
    ValueSourceStandard? Rows = null,
    ValueSourceStandard? Items = null,
    ExprStandard? RowValue = null,
    ExprStandard? Filter = null,
    string? Separator = null,
    string? OnBlankFirstKey = null,
    string? OnDuplicate = null,
    bool Distinct = false,
    string? ElementType = null,
    NewStandard? Construct = null,
    string? Named = null,
    DelegateStandard? Delegate = null,
    bool? Json = null,
    string? Content = null,
    IReadOnlyList<ValueSourceStandard>? ListItems = null,
    IReadOnlyList<PairStandard>? Pairs = null,
    ValueSourceStandard? JsonText = null,
    ValueSourceStandard? JsonOptions = null);

/// <summary>字典字面量键值对（<c>from:map</c> 的 <c>pairs</c> 项；键 / 值各自为取值来源）。</summary>
/// <param name="Key">键来源（多为字符串字面量）。</param>
/// <param name="Value">值来源。</param>
public sealed record PairStandard(ValueSourceStandard Key, ValueSourceStandard Value);
