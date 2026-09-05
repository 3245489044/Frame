using MetaCommand;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Models
{
    [TestClass]
    public class ErrorCodeTests
    {
        [TestMethod]
        public void GetMessage_KnownCode_ReturnsMessage()
        {
            Assert.AreEqual("Success", ErrorCode.None.GetMessage());
            Assert.AreEqual("Invalid argument", ErrorCode.InvalidArgument.GetMessage());
            Assert.AreEqual("Command cannot be empty", ErrorCode.CommandEmpty.GetMessage());
            Assert.AreEqual("An unknown error occurred", ErrorCode.UnknownError.GetMessage());
        }

        [TestMethod]
        public void GetMessage_UnknownCode_ReturnsUnknownErrorMessage()
        {
            var unknownCode = (ErrorCode)9999;
            Assert.AreEqual("Unknown error", unknownCode.GetMessage());
        }

        [TestMethod]
        public void IsSuccess_NoneCode_ReturnsTrue()
            => Assert.IsTrue(ErrorCode.None.IsSuccess());

        [TestMethod]
        public void IsSuccess_NonNoneCode_ReturnsFalse()
            => Assert.IsFalse(ErrorCode.InvalidArgument.IsSuccess());

        [TestMethod]
        public void IsError_NoneCode_ReturnsFalse()
            => Assert.IsFalse(ErrorCode.None.IsError());

        [TestMethod]
        public void IsError_NonNoneCode_ReturnsTrue()
            => Assert.IsTrue(ErrorCode.InvalidArgument.IsError());

        [TestMethod]
        public void IsFatal_CodeBelow1500_ReturnsFalse()
        {
            Assert.IsFalse(ErrorCode.None.IsFatal());
            Assert.IsFalse(ErrorCode.InvalidArgument.IsFatal());
            Assert.IsFalse(ErrorCode.ExecutionFailed.IsFatal());
        }

        [TestMethod]
        public void IsFatal_Code1500OrAbove_ReturnsTrue()
        {
            Assert.IsTrue(ErrorCode.StorageError.IsFatal());
            Assert.IsTrue(ErrorCode.DataNotFound.IsFatal());
            Assert.IsTrue(ErrorCode.SerializationError.IsFatal());
        }
    }
}
