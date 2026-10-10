using System.Text;
using System.Text.Json.Serialization;

namespace Plate.DslMethod.Usharp;

/// <summary>USharp 数据层标量种类。</summary>
public enum UsharpScalarKind
{
    String,
    Int,
    Decimal,
    Bool,
    Null,
}

/// <summary>
/// USharp 数据层值模型（「声明 + 数据」双用语言的**数据半边**）。
/// <para>路线 B：本模型下沉至中立库（<c>Plate.DslMethod</c>），成为**全局通用数据表示**——
/// 存储层与节点层可直接承载 USharp 值，删除中立 JSON 边界。</para>
/// </summary>
[JsonConverter(typeof(UsharpValueConverter))]
public abstract record UsharpValue
{
    /// <summary>规范形文本（写盘 / 对拍 / diff 的**单一来源**）。</summary>
    public abstract string ToCanonical();

    /// <summary>缩进式多行渲染（记录声明块用）。</summary>
    public abstract void Write(StringBuilder sb, int indent);

    public override string ToString() => ToCanonical();
}

/// <summary>标量值（字符串 / 整数 / 小数 / 布尔 / null）。</summary>
public sealed record UsharpScalar(UsharpScalarKind Kind, string Text) : UsharpValue
{
    public static UsharpScalar Str(string? value) => new(UsharpScalarKind.String, value ?? string.Empty);

    public static UsharpScalar Int(long value) => new(UsharpScalarKind.Int, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static UsharpScalar Dec(decimal value) => new(UsharpScalarKind.Decimal, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static UsharpScalar Bool(bool value) => new(UsharpScalarKind.Bool, value ? "true" : "false");

    public static readonly UsharpScalar Null = new(UsharpScalarKind.Null, "null");

    public string? AsString() => Kind == UsharpScalarKind.String ? Text : null;

    public override string ToCanonical() => Kind switch
    {
        UsharpScalarKind.String => Quote(Text),
        UsharpScalarKind.Null => "null",
        _ => Text,
    };

    public override void Write(StringBuilder sb, int indent) => sb.Append(ToCanonical());

    /// <summary>字符串引号 + 转义（规范形单源）。</summary>
    public static string Quote(string? s)
    {
        s ??= string.Empty;
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>列表值。</summary>
public sealed record UsharpList(IReadOnlyList<UsharpValue> Items) : UsharpValue
{
    public override string ToCanonical()
        => "[" + string.Join(", ", Items.Select(i => i.ToCanonical())) + "]";

    public override void Write(StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent + 2);
        if (Items.Count == 0) { sb.Append("[]"); return; }
        sb.Append('[').Append('\n');
        for (var i = 0; i < Items.Count; i++)
        {
            sb.Append(pad);
            Items[i].Write(sb, indent + 2);
            if (i < Items.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(new string(' ', indent)).Append(']');
    }
}

/// <summary>记录值（字段序 = 声明序，规范形禁重排）。<see cref="Fields"/> 容器为**可变后盾**
/// （<see cref="Set"/>/<see cref="Remove"/>），供运行态薄视图（如 <c>NodeRecord</c>）直读写**同一实例**（非拷贝）。</summary>
public sealed record UsharpRecord : UsharpValue
{
    private readonly List<KeyValuePair<string, UsharpValue>> _fields;

    public UsharpRecord(IReadOnlyList<KeyValuePair<string, UsharpValue>> fields)
        => _fields = new List<KeyValuePair<string, UsharpValue>>(fields ?? Array.Empty<KeyValuePair<string, UsharpValue>>());

    /// <summary>字段序列（视图；记录序 = 声明序）。</summary>
    public IReadOnlyList<KeyValuePair<string, UsharpValue>> Fields => _fields;

    public bool TryGet(string name, out UsharpValue value)
    {
        foreach (var f in _fields)
            if (string.Equals(f.Key, name, StringComparison.Ordinal)) { value = f.Value; return true; }
        value = UsharpScalar.Null;
        return false;
    }

    /// <summary>视图写入：同名**保位替换** / 新增**追加**（同一实例直写，非拷贝）。</summary>
    public void Set(string name, UsharpValue value)
    {
        for (var i = 0; i < _fields.Count; i++)
            if (string.Equals(_fields[i].Key, name, StringComparison.Ordinal)) { _fields[i] = new KeyValuePair<string, UsharpValue>(name, value); return; }
        _fields.Add(new KeyValuePair<string, UsharpValue>(name, value));
    }

    /// <summary>视图删除（不存在 ⇒ false）。</summary>
    public bool Remove(string name)
    {
        for (var i = 0; i < _fields.Count; i++)
            if (string.Equals(_fields[i].Key, name, StringComparison.Ordinal)) { _fields.RemoveAt(i); return true; }
        return false;
    }

    public override string ToCanonical()
        => "{ " + string.Join(", ", _fields.Select(f => f.Key + " = " + f.Value.ToCanonical())) + " }";

    public override void Write(StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent + 2);
        if (_fields.Count == 0) { sb.Append("{}"); return; }
        sb.Append('{').Append('\n');
        foreach (var f in _fields)
        {
            sb.Append(pad).Append(f.Key).Append(" = ");
            f.Value.Write(sb, indent + 2);
            sb.Append('\n');
        }
        sb.Append(new string(' ', indent)).Append('}');
    }

    /// <summary>值等价（字段序逐项比较，屏蔽可变后盾对 record 默认引用等价的影响）。</summary>
    public bool Equals(UsharpRecord? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_fields.Count != other._fields.Count) return false;
        for (var i = 0; i < _fields.Count; i++)
            if (!string.Equals(_fields[i].Key, other._fields[i].Key, StringComparison.Ordinal)
                || !Equals(_fields[i].Value, other._fields[i].Value)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var f in _fields) { h.Add(f.Key); h.Add(f.Value); }
        return h.ToHashCode();
    }
}
