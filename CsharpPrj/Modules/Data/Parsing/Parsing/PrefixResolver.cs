
using System.Text.RegularExpressions;

namespace Modules.Parsing.Parsing
{
    public static class PrefixResolver
    {
        public const string Style = "@style.";
        public const string Template = "@template.";
        public const string Default = "@default.";
        public const string Input = "@input.";
        public const string Node = "@node.";
        public const string Prev = "@prev";
        public const string Extends = "@extends.";

        private static readonly Regex InputReferenceRegex = new(
            @"@input\.([\w.]+)", RegexOptions.Compiled);


        public static bool IsReference(string value, string prefix) =>
            value.StartsWith(prefix);

        public static string ResolveName(string value, string prefix)
        {
            if (value.StartsWith(prefix))
                return value[prefix.Length..];
            return value;
        }

        public static List<string> ExtractInputReferences(string value)
        {
            var matches = InputReferenceRegex.Matches(value);
            var results = new List<string>(matches.Count);
            foreach (Match match in matches)
            {
                results.Add(match.Value);
            }
            return results;
        }

        public static string ReplaceInputReference(string value, string paramName, string replacement)
        {
            return InputReferenceRegex.Replace(value, m =>
            {
                var name = m.Groups[1].Value;
                return name == paramName ? replacement : m.Value;
            });
        }

        public static string ReplaceAllInputReferences(string value, Dictionary<string, string> parameters)
        {
            return InputReferenceRegex.Replace(value, m =>
            {
                var name = m.Groups[1].Value;
                return parameters.TryGetValue(name, out var replacement) ? replacement : m.Value;
            });
        }


        [Obsolete("Use IsReference(value, PrefixResolver.Style) instead")]
        public static bool IsStyleReference(string value) => IsReference(value, Style);
        [Obsolete("Use IsReference(value, PrefixResolver.Template) instead")]
        public static bool IsTemplateReference(string value) => IsReference(value, Template);
        [Obsolete("Use IsReference(value, PrefixResolver.Default) instead")]
        public static bool IsDefaultReference(string value) => IsReference(value, Default);
        [Obsolete("Use IsReference(value, PrefixResolver.Input) instead")]
        public static bool IsInputReference(string value) => IsReference(value, Input);
        [Obsolete("Use IsReference(value, PrefixResolver.Node) instead")]
        public static bool IsNodeReference(string value) => IsReference(value, Node);
        [Obsolete("Use value == PrefixResolver.Prev instead")]
        public static bool IsPrevReference(string value) => value == Prev;
        [Obsolete("Use IsReference(value, PrefixResolver.Extends) instead")]
        public static bool IsExtendsReference(string value) => IsReference(value, Extends);

        [Obsolete("Use ResolveName(value, PrefixResolver.Style) instead")]
        public static string ExtractStyleName(string styleRef) => ResolveName(styleRef, Style);
        [Obsolete("Use ResolveName(value, PrefixResolver.Template) instead")]
        public static string ExtractTemplateName(string templateRef) => ResolveName(templateRef, Template);
        [Obsolete("Use ResolveName(value, PrefixResolver.Default) instead")]
        public static string ExtractDefaultName(string defaultRef) => ResolveName(defaultRef, Default);
        [Obsolete("Use ResolveName(value, PrefixResolver.Input) instead")]
        public static string ExtractInputName(string inputRef) => ResolveName(inputRef, Input);
        [Obsolete("Use ResolveName(value, PrefixResolver.Node) instead")]
        public static string ExtractNodeName(string nodeRef) => ResolveName(nodeRef, Node);
        [Obsolete("Use ResolveName(value, PrefixResolver.Extends) instead")]
        public static string ExtractExtendsName(string extendsRef) => ResolveName(extendsRef, Extends);

        [Obsolete("Use ResolveName(value, PrefixResolver.Style) instead")]
        public static string ResolveStyleName(string value) => ResolveName(value, Style);
        [Obsolete("Use ResolveName(value, PrefixResolver.Template) instead")]
        public static string ResolveTemplateName(string value) => ResolveName(value, Template);
        [Obsolete("Use ResolveName(value, PrefixResolver.Default) instead")]
        public static string ResolveDefaultName(string value) => ResolveName(value, Default);
        [Obsolete("Use ResolveName(value, PrefixResolver.Input) instead")]
        public static string ResolveInputName(string value) => ResolveName(value, Input);
        [Obsolete("Use ResolveName(value, PrefixResolver.Node) instead")]
        public static string ResolveNodeName(string value) => ResolveName(value, Node);
        [Obsolete("Use ResolveName(value, PrefixResolver.Extends) instead")]
        public static string ResolveExtendsName(string value) => ResolveName(value, Extends);
    }
}