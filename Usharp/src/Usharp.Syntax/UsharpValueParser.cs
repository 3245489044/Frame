using System.Globalization;
using System.Text;

namespace Usharp.Syntax;

/// <summary>
/// USharp 数据层值解析器（递归下降；零依赖）。
/// 文法：<c>&lt;value&gt; ::= scalar | list | record</c>；<c>&lt;list&gt; ::= "[" (value ("," value)*)? "]"</c>；
/// <c>&lt;record&gt; ::= "{" (ident "=" value ("," ident "=" value)*)? "}"</c>。
/// 记录续行块（<c>ident = &lt;value&gt;</c> 每行一条）由 <see cref="ParseRecordBlock"/> 承担。
/// </summary>
public static class UsharpValueParser
{
    /// <summary>解析单个值（须恰好消费整串；失败给出行内原因）。</summary>
    public static bool TryParse(string? text, out UsharpValue value, out string error)
    {
        value = UsharpScalar.Null;
        error = string.Empty;
        var parser = new Cursor(text ?? string.Empty);
        if (!parser.TryValue(out value)) { error = parser.Error; return false; }
        parser.SkipWs();
        if (!parser.Eof) { error = "尾随未解析内容：" + parser.Rest(); return false; }
        return true;
    }

    /// <summary>
    /// 记录续行块：每行 <c>&lt;ident&gt; = &lt;value&gt;</c>（值须在**单行内**闭合）。
    /// 无法解析的行原文落入 <paramref name="unparsed"/>（禁静默丢弃；闸A 据其报码）。
    /// </summary>
    public static UsharpRecord ParseRecordBlock(IEnumerable<string> lines, List<string> unparsed)
    {
        ArgumentNullException.ThrowIfNull(lines);
        unparsed ??= new List<string>();

        var fields = new List<KeyValuePair<string, UsharpValue>>();
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) { unparsed.Add(line); continue; }

            var name = line[..eq].Trim();
            var valueText = line[(eq + 1)..].Trim();
            if (!UsharpIdentifier.Matches(name)) { unparsed.Add(line); continue; }
            if (!TryParse(valueText, out var value, out _)) { unparsed.Add(line); continue; }

            fields.Add(new KeyValuePair<string, UsharpValue>(name, value));
        }
        return new UsharpRecord(fields);
    }

    /// <summary>把值对象渲染为规范形文本（单行）。</summary>
    public static string Canonical(UsharpValue value) => (value ?? UsharpScalar.Null).ToCanonical();

    private sealed class Cursor
    {
        private readonly string _s;
        private int _i;

        public Cursor(string s) => _s = s;

        public bool Eof => _i >= _s.Length;

        public string Error { get; private set; } = string.Empty;

        public string Rest() => _s[_i..];

        public void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        public bool TryValue(out UsharpValue value)
        {
            value = UsharpScalar.Null;
            SkipWs();
            if (Eof) { Error = "空值"; return false; }

            var c = _s[_i];
            if (c == '[') return TryList(out value);
            if (c == '{') return TryRecord(out value);
            if (c == '"') return TryString(out value);
            return TryBare(out value);
        }

        private bool TryList(out UsharpValue value)
        {
            value = UsharpScalar.Null;
            _i++;                       // '['
            var items = new List<UsharpValue>();
            SkipWs();
            if (!Eof && _s[_i] == ']') { _i++; value = new UsharpList(items); return true; }

            while (true)
            {
                if (!TryValue(out var item)) return false;
                items.Add(item);
                SkipWs();
                if (Eof) { Error = "列表未闭合"; return false; }
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; value = new UsharpList(items); return true; }
                Error = "列表分隔符非法"; return false;
            }
        }

        private bool TryRecord(out UsharpValue value)
        {
            value = UsharpScalar.Null;
            _i++;                       // '{'
            var fields = new List<KeyValuePair<string, UsharpValue>>();
            SkipWs();
            if (!Eof && _s[_i] == '}') { _i++; value = new UsharpRecord(fields); return true; }

            while (true)
            {
                SkipWs();
                if (!UsharpIdentifier.TryRead(_s, ref _i, out var name)) { Error = "记录字段名非法"; return false; }
                SkipWs();
                if (Eof || _s[_i] != '=') { Error = "记录字段缺 '='"; return false; }
                _i++;
                if (!TryValue(out var fv)) return false;
                fields.Add(new KeyValuePair<string, UsharpValue>(name, fv));
                SkipWs();
                if (Eof) { Error = "记录未闭合"; return false; }
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; value = new UsharpRecord(fields); return true; }
                Error = "记录分隔符非法"; return false;
            }
        }

        private bool TryString(out UsharpValue value)
        {
            value = UsharpScalar.Null;
            _i++;                       // '"'
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                var c = _s[_i++];
                if (c == '"') { value = UsharpScalar.Str(sb.ToString()); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) { Error = "转义未完成"; return false; }
                var e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) { Error = "\\u 转义不完整"; return false; }
                        if (!ushort.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        { Error = "\\u 转义非法"; return false; }
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: Error = "非法转义 \\" + e; return false;
                }
            }
            Error = "字符串未闭合";
            return false;
        }

        private bool TryBare(out UsharpValue value)
        {
            value = UsharpScalar.Null;
            var start = _i;
            while (_i < _s.Length && !IsBareTerminator(_s[_i])) _i++;
            var token = _s[start.._i];
            if (token.Length == 0) { Error = "空 token"; return false; }

            if (token == "null") { value = UsharpScalar.Null; return true; }
            if (token == "true") { value = UsharpScalar.Bool(true); return true; }
            if (token == "false") { value = UsharpScalar.Bool(false); return true; }

            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            { value = UsharpScalar.Int(l); return true; }

            if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            { value = UsharpScalar.Dec(d); return true; }

            Error = "无法识别的字面量 '" + token + "'（字符串须加引号）";
            return false;
        }

        private static bool IsBareTerminator(char c)
            => c is ',' or ']' or '}' or '=' || char.IsWhiteSpace(c);
    }
}

