using System;

namespace Plate.DslMethod.Usharp;

/// <summary>
/// USharp 对象映射忽略标记（B3a）：标注的属性不进入 <c>UsharpValueMapper</c> 的读写面。
/// <para>用途 = 持久化单一载体：同一事实只允许一个落盘字段；运行时镜像字段标注本属性，
/// 由权威字段在读取末尾回填（禁静默丢弃，见 <c>AX.NoSilentOutlet</c>）。</para>
/// <para>仅影响 USharp 文档映射；System.Text.Json（API/JSON 载荷）不受影响。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UsharpIgnoreAttribute : Attribute
{
}
