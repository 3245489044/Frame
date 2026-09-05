using MetaCommand;
using MetaCommand.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Utilities
{
    [TestClass]
    public class EnumResolverTests
    {
        [TestMethod]
        public void Resolve_ValidString_ReturnsParsedValue()
        {
            var value = EnumResolver.Resolve<MetaVerb>("Do");
            Assert.AreEqual(MetaVerb.Do, value);
        }

        [TestMethod]
        public void Resolve_CaseInsensitive_ReturnsParsedValue()
        {
            var value = EnumResolver.Resolve<MetaVerb>("do");
            Assert.AreEqual(MetaVerb.Do, value);
        }

        [TestMethod]
        public void Resolve_InvalidString_ReturnsFallback()
        {
            var value = EnumResolver.Resolve<MetaVerb>("invalid", MetaVerb.Do);
            Assert.AreEqual(MetaVerb.Do, value);
        }

        [TestMethod]
        public void Resolve_EmptyString_ReturnsFallback()
        {
            var value = EnumResolver.Resolve<MetaVerb>("", MetaVerb.Get);
            Assert.AreEqual(MetaVerb.Get, value);
        }

        [TestMethod]
        public void Resolve_NullString_ReturnsFallback()
        {
            var value = EnumResolver.Resolve<MetaVerb>(null!, MetaVerb.Look);
            Assert.AreEqual(MetaVerb.Look, value);
        }

        [TestMethod]
        public void Resolve_AllVerbs_ResolvesCorrectly()
        {
            Assert.AreEqual(MetaVerb.Do, EnumResolver.Resolve<MetaVerb>("Do"));
            Assert.AreEqual(MetaVerb.Get, EnumResolver.Resolve<MetaVerb>("Get"));
            Assert.AreEqual(MetaVerb.Look, EnumResolver.Resolve<MetaVerb>("Look"));
            Assert.AreEqual(MetaVerb.Wait, EnumResolver.Resolve<MetaVerb>("Wait"));
            Assert.AreEqual(MetaVerb.Assert, EnumResolver.Resolve<MetaVerb>("Assert"));
        }
    }
}
