
#region USINGS
extern alias ShareAlias;
using System;
using System.Collections.Generic;
using System.Linq;
using ShareAlias::ReferenceManagement;
using ShareAlias::ReferenceManagement.FieldMapping;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.ReferenceRegistration
{
    /// <summary>
    /// 节点配置引用字段清单（<b>唯一声明源</b>）：既驱动 Share 引用注册表的访问器条件，
    /// 也注入框架 <c>NodeReferenceIndex</c>（同一清单，禁两处各写字面量）。
    /// 后缀匹配形态仅作<b>声明来源之一</b>（不得作唯一判据）。
    /// </summary>
    public static class NodeReferenceFields
    {
        public const string AddConfigAddKey = "AddConfigAddKey";
        public const string ConnectChainInfoKey = "ConnectChainInfoKey";
        public const string ParameterKeys = "ParameterKeys";
        public const string ResultKeys = "ResultKeys";

        /// <param name="Name">引用字段名（注入框架索引的清单元素）。</param>
        /// <param name="Suffix">属性/字段描述的后缀匹配形态（声明值，非硬编码）。</param>
        /// <param name="AllowField">是否允许命中字段访问器（默认仅属性访问器）。</param>
        public sealed record ReferenceField(string Name, string Suffix, bool AllowField = false);

        public static readonly IReadOnlyList<ReferenceField> Declared = new[]
        {
            new ReferenceField(AddConfigAddKey, AddConfigAddKey, AllowField: true),
            new ReferenceField(ConnectChainInfoKey, "." + ConnectChainInfoKey),
            new ReferenceField(ParameterKeys, "." + ParameterKeys),
            new ReferenceField(ResultKeys, "." + ResultKeys),
        };

        /// <summary>引用字段名清单（注入框架 NodeReferenceIndex 的字段面）。</summary>
        public static IReadOnlyList<string> FieldNames =>
            Declared.Select(entry => entry.Name).ToArray();

        public static ReferenceField Field(string name) =>
            Declared.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(name), name, "未声明的引用字段");

        /// <summary>按声明产访问器命中条件（后缀 + 访问器种类均来自声明）。</summary>
        public static Func<IValueAccessor, bool> Matches(ReferenceField referenceField) =>
            accessor => accessor switch
            {
                PropertyAccessor property => property.Description.EndsWith(referenceField.Suffix, StringComparison.Ordinal),
                FieldAccessor propertyField when referenceField.AllowField => propertyField.Description.EndsWith(referenceField.Suffix, StringComparison.Ordinal),
                _ => false,
            };

        /// <summary>框架引用索引端口（注入 <c>NodeFrameworkOptions.ReferenceIndex</c>）。</summary>
        public static Modules.Node.Spec.IReferenceIndexPort CreateIndexPort() =>
            new Modules.Node.Spec.NodeReferenceIndex(FieldNames);
    }
}
