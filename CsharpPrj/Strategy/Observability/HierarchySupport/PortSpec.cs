using System;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 端口契约（工作流容器对外输入/输出接口）。InputPorts 用 Key+IsRequired；
    /// OutputPorts 用 Key+SourceKey（来源内部键）。ValueType/DefaultValue/Description 为可选文档/提示。
    /// </summary>
    public sealed class PortSpec
    {
        public string Key { get; init; } = "";

        /// <summary>OutputPorts 用：来源内部键（缺省取 Key）。</summary>
        public string? SourceKey { get; init; }

        /// <summary>可选：类型提示。</summary>
        public Type? ValueType { get; init; }

        /// <summary>可选：缺省值。</summary>
        public object? DefaultValue { get; init; }

        /// <summary>可选：文档。</summary>
        public string? Description { get; init; }

        /// <summary>InputPorts 用：是否必需输入。</summary>
        public bool IsRequired { get; init; } = true;
    }
}
