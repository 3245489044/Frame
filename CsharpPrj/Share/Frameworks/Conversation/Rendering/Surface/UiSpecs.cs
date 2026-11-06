#nullable enable

using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>槽位在某载体上的绑定（<c>bind</c> 为受控词：cssvar / prop / none）。</summary>
/// <param name="CarrierId">载体 id（声明面键）。</param>
/// <param name="Bind">绑定方式。</param>
/// <param name="Prop">载体属性名（cssvar ⇒ 变量名；prop ⇒ 原生属性名）。</param>
/// <param name="Unit">单位（载体侧声明；token 值保持单位less）。</param>
public sealed record UiSlotBinding(string CarrierId, string Bind, string Prop, string Unit = "");

/// <summary>槽位声明（载体无关）：一个语义槽位在各载体上如何落地。</summary>
/// <param name="Name">槽位名。</param>
/// <param name="Bindings">载体 id → 绑定。</param>
public sealed record UiSlotSpec(string Name, IReadOnlyDictionary<string, UiSlotBinding> Bindings);

/// <summary>语义值声明（真实取值只来自此处）。</summary>
/// <param name="Name">值名。</param>
/// <param name="Kind">类别（color/font/size/space/radius…）。</param>
/// <param name="Value">取值。</param>
public sealed record UiTokenSpec(string Name, string Kind, string Value);

/// <summary>皮肤声明（个性化档）：继承链 + token 覆盖。</summary>
/// <param name="Name">皮肤名。</param>
/// <param name="Extends">父皮肤（可空）。</param>
/// <param name="Density">密度档（可空）。</param>
/// <param name="IsDefault">是否缺省皮肤（声明面 <c>default = "true"</c>）。</param>
/// <param name="Tokens">token 覆盖。</param>
public sealed record UiSkinSpec(
    string Name, string? Extends, string Density, bool IsDefault, IReadOnlyDictionary<string, string> Tokens);

/// <summary>
/// 渲染单元声明（**唯一脊梁**）：元素（载体差）+ 槽位（视觉语义）+ 状态覆盖，三载体共用一条。
/// </summary>
/// <param name="Name">单元名（role/cell/class 键）。</param>
/// <param name="Kind">role | cell | class。</param>
/// <param name="Slots">槽位 → token 名。</param>
/// <param name="States">状态 → （槽位 → token 名）覆盖。</param>
/// <param name="Carriers">载体 id → 结构属性（element/control/cls/prefix…）。</param>
public sealed record UiPartSpec(
    string Name,
    string Kind,
    IReadOnlyDictionary<string, string> Slots,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> States,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Carriers);
