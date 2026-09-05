using MetaCommand;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Models
{
    [TestClass]
    public class OperationResultTests
    {
        [TestMethod]
        public void SuccessResult_WithDataAndMessage_ReturnsSuccessResult()
        {
            var result = OperationResult<object>.SuccessResult("data", "success");
            Assert.IsTrue(result.Success);
            Assert.AreEqual("data", result.Data);
            Assert.AreEqual("success", result.Message);
            Assert.AreEqual(ErrorCode.None, result.ErrorCode);
            Assert.IsNull(result.Exception);
        }

        [TestMethod]
        public void SuccessResult_WithNullData_ReturnsSuccessResult()
        {
            var result = OperationResult<object>.SuccessResult(null, "success");
            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Data);
        }

        [TestMethod]
        public void FailureResult_WithMessageAndErrorCode_ReturnsFailureResult()
        {
            var result = OperationResult<object>.FailureResult("error", ErrorCode.InvalidArgument);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("error", result.Message);
            Assert.AreEqual(ErrorCode.InvalidArgument, result.ErrorCode);
            Assert.IsNull(result.Data);
            Assert.IsNull(result.Exception);
        }

        [TestMethod]
        public void FailureResult_WithException_ReturnsFailureResultWithException()
        {
            var ex = new InvalidOperationException("test");
            var result = OperationResult<object>.FailureResult("error", ErrorCode.ExecutionFailed, ex);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("error", result.Message);
            Assert.AreEqual(ErrorCode.ExecutionFailed, result.ErrorCode);
            Assert.AreSame(ex, result.Exception);
        }

        [TestMethod]
        public void FailureResult_MessageOnly_UsesUnknownError()
        {
            var result = OperationResult<object>.FailureResult("error message");
            Assert.IsFalse(result.Success);
            Assert.AreEqual("error message", result.Message);
            Assert.AreEqual(ErrorCode.UnknownError, result.ErrorCode);
            Assert.IsNull(result.Exception);
        }

        [TestMethod]
        public void ToObjectResult_Success_ConvertsCorrectly()
        {
            var genericResult = OperationResult<object>.SuccessResult("data", "success");
            var objectResult = genericResult.ToObjectResult();
            Assert.IsTrue(objectResult.Success);
            Assert.AreEqual("success", objectResult.Message);
            Assert.AreEqual(ErrorCode.None, objectResult.ErrorCode);
        }

        [TestMethod]
        public void ToObjectResult_Failure_ConvertsCorrectly()
        {
            var genericResult = OperationResult<object>.FailureResult("error", ErrorCode.InvalidArgument);
            var objectResult = genericResult.ToObjectResult();
            Assert.IsFalse(objectResult.Success);
            Assert.AreEqual("error", objectResult.Message);
            Assert.AreEqual(ErrorCode.InvalidArgument, objectResult.ErrorCode);
        }

        [TestMethod]
        public void IsFatalError_FatalErrorCode_ReturnsTrue()
        {
            var result = OperationResult<object>.FailureResult("fatal", ErrorCode.StorageError);
            Assert.IsTrue(result.IsFatalError());
        }

        [TestMethod]
        public void IsFatalError_NonFatalErrorCode_ReturnsFalse()
        {
            var result = OperationResult<object>.FailureResult("non-fatal", ErrorCode.InvalidArgument);
            Assert.IsFalse(result.IsFatalError());
        }

        [TestMethod]
        public void ToString_ReturnsFormattedString()
        {
            var result = OperationResult<object>.SuccessResult("data", "success");
            var str = result.ToString();
            Assert.IsTrue(str.Contains("Success: True"));
            Assert.IsTrue(str.Contains("success"));
        }
    }
}
