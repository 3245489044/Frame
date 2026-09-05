using System.Text;

namespace Plate.XmlMethod.Xml
{
    /// <summary>
    /// 流式 XML 元素构建器：声明一个元素，追加属性与子节点，最后 <see cref="Render"/> 产出稳定的 XML 字符串。
    /// - 属性值 / 子文本内容一律经 <see cref="XmlText.Escape"/> 自动转义，杜绝注入。
    /// - 子节点按追加顺序渲染；无子节点且自闭合时输出自闭合标签（如 &lt;rect/&gt;）。
    /// </summary>
    public sealed class XmlElement
    {
        private readonly string _name;
        private readonly List<KeyValuePair<string, string>> _attributes = new();
        private readonly List<object> _children = new(); // string=文本(转义) | XmlElement

        public XmlElement(string name)
        {
            _name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("XML 元素名不能为空。", nameof(name))
                : name;
        }

        public string Name => _name;

        /// <summary>追加一个属性；值为 null 时忽略（便于按条件补属性），bool 转小写。同名属性会覆盖旧值，避免重复属性。</summary>
        public XmlElement Attr(string name, object? value)
        {
            if (value == null)
                return this;
            var existing = _attributes.FindIndex(kv => string.Equals(kv.Key, name, StringComparison.Ordinal));
            if (existing >= 0)
                _attributes[existing] = new(name, XmlText.ToAttrValue(value));
            else
                _attributes.Add(new(name, XmlText.ToAttrValue(value)));
            return this;
        }

        /// <summary>追加一组属性（属性名, 值) 元组。</summary>
        public XmlElement Attrs(params (string Name, object? Value)[] attrs)
        {
            foreach (var (name, value) in attrs)
                Attr(name, value);
            return this;
        }

        /// <summary>追加子元素并返回父级：child.Child("g").Attr("id","x") 后可链回父元素继续追加。</summary>
        public XmlElement Child(XmlElement child)
        {
            _children.Add(child);
            return this;
        }

        /// <summary>便捷：创建一个子元素并用 configure 就地配置（可再用 configure 内的 .Child 继续嵌套）。</summary>
        public XmlElement ChildElement(string name, Action<XmlElement> configure)
        {
            var el = new XmlElement(name);
            configure(el);
            return Child(el);
        }

        /// <summary>追加文本内容（自动转义，防注入）。</summary>
        public XmlElement Text(string? text)
        {
            if (!string.IsNullOrEmpty(text))
                _children.Add(XmlText.Escape(text));
            return this;
        }

        /// <summary>追加一段已按 XML 转义好的原始片段（仅供已确认安全的调用方使用）。</summary>
        public XmlElement Raw(string? xml)
        {
            if (!string.IsNullOrEmpty(xml))
                _children.Add(xml);
            return this;
        }

        /// <summary>子节点（含文本与元素）数量，用于判断是否自闭合。</summary>
        public int ChildCount => _children.Count;

        /// <summary>渲染成 XML 字符串。pretty = true 时对元素做两级缩进，便于调试/人工阅读。</summary>
        public string Render(bool pretty = false)
        {
            var sb = new StringBuilder();
            AppendTo(sb, pretty ? 0 : -1);
            return sb.ToString();
        }

        public override string ToString() => Render();

        private void AppendTo(StringBuilder sb, int indent)
        {
            if (indent >= 0)
            {
                sb.Append('\n');
                sb.Append(' ', indent * 2);
            }

            sb.Append('<').Append(_name);
            foreach (var (name, value) in _attributes)
                sb.Append(' ').Append(name).Append("=\"").Append(XmlText.Escape(value)).Append('"');

            if (_children.Count == 0)
            {
                sb.Append("/>");
                return;
            }

            sb.Append('>');
            foreach (var child in _children)
            {
                if (child is XmlElement el)
                {
                    el.AppendTo(sb, indent >= 0 ? indent + 1 : -1);
                }
                else
                {
                    sb.Append(child); // 已是转义文本或受信任原始片段
                }
            }

            if (indent >= 0)
            {
                sb.Append('\n');
                sb.Append(' ', indent * 2);
            }
            sb.Append("</").Append(_name).Append('>');
        }
    }
}