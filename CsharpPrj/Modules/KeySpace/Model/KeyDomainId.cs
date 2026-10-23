using System;

namespace Modules.KeySpace.Model;

/// <summary>
/// 域标识（不透明受控词）：域词表由声明文件给出，框架源码不出现具体域字面量（KS5）。
/// <para>跨域传递必须携带本标识；空值 = 未声明域（<see cref="Undeclared"/>），严格模式下不得进入持久化或跨域边界。</para>
/// </summary>
public readonly record struct KeyDomainId
{
    /// <summary>未声明域（默认值；严格模式门禁判违规）。</summary>
    public static readonly KeyDomainId Undeclared = new(string.Empty);

    /// <summary>域标识文本（声明文件中的域词）。</summary>
    public string Id { get; }

    private KeyDomainId(string id) => Id = id;

    /// <summary>是否已声明域（非空白）。</summary>
    public bool IsDeclared => !string.IsNullOrWhiteSpace(Id);

    /// <summary>构造域标识（仅校验非空白；「域是否已登记」由 <see cref="Rules.KeyDomainRegistry"/> 裁决）。</summary>
    public static KeyDomainId Of(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("域标识不得为空", nameof(id));
        return new KeyDomainId(id.Trim());
    }

    public override string ToString() => IsDeclared ? Id : "(undeclared)";
}
