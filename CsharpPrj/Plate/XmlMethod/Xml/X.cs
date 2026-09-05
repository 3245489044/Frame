namespace Plate.XmlMethod.Xml
{
    /// <summary>
    /// 便捷入口门面：用简洁的 <c>X.Elem("rect")</c> 方式创建 <see cref="XmlElement"/>，减少样板代码。
    /// 示例：<c>X.Elem("rect").Attr("x",0).Attr("y",0).Attr("width",180).Attr("height",44)</c>
    /// </summary>
    public static class X
    {
        public static XmlElement Elem(string name) => new(name);
    }
}