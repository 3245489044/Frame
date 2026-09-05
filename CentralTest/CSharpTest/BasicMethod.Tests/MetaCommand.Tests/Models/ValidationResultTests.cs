using MetaCommand;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Models
{
    [TestClass]
    public class ValidationResultTests
    {
        [TestMethod]
        public void Success_ReturnsValidResult()
        {
            var validationResult = ValidationResult.Success();
            Assert.IsTrue(validationResult.IsValid);
            Assert.AreEqual(0, validationResult.Errors.Count);
        }

        [TestMethod]
        public void Constructor_WithErrors_IsValidIsFalse()
        {
            var errors = new List<ValidationError>
            {
                new ValidationError("Action", "Action is required")
            };
            var validationResult = new ValidationResult(errors);
            Assert.IsFalse(validationResult.IsValid);
            Assert.AreEqual(1, validationResult.Errors.Count);
        }

        [TestMethod]
        public void Constructor_WithNullErrors_ReturnsValidResult()
        {
            var validationResult = new ValidationResult(null!);
            Assert.IsTrue(validationResult.IsValid);
            Assert.AreEqual(0, validationResult.Errors.Count);
        }

        [TestMethod]
        public void ValidationError_Constructor_SetsProperties()
        {
            var error = new ValidationError("Target", "Target is required");
            Assert.AreEqual("Target", error.PropertyName);
            Assert.AreEqual("Target is required", error.ErrorMessage);
        }

        [TestMethod]
        public void ValidationError_NullPropertyName_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ValidationError(null!, "error"));
        }

        [TestMethod]
        public void ValidationError_NullErrorMessage_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ValidationError("Prop", null!));
        }
    }
}
