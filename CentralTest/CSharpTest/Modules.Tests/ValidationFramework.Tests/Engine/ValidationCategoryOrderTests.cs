
using ValidationFramework.Abstractions;
using ValidationFramework.Engine;

namespace ValidationFramework.Tests.Engine;

[TestClass]
public class ValidationCategoryOrderTests
{
    [TestMethod]
    public void Default_应按五分类顺序排列()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                ValidationRuleCategory.Structure,
                ValidationRuleCategory.Reference,
                ValidationRuleCategory.Semantic,
                ValidationRuleCategory.Runtime,
                ValidationRuleCategory.Contract
            },
            ValidationCategoryOrder.Default);
    }

    [TestMethod]
    public void IsEnabled_结构分类_默认启用()
    {
        var options = new ValidationEngineOptions();

        Assert.IsTrue(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Structure));
        Assert.IsTrue(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Reference));
        Assert.IsTrue(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Semantic));
    }

    [TestMethod]
    public void IsEnabled_运行时与契约_默认禁用()
    {
        var options = new ValidationEngineOptions();

        Assert.IsFalse(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Runtime));
        Assert.IsFalse(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Contract));
    }

    [TestMethod]
    public void IsEnabled_应反映选项开关()
    {
        var options = new ValidationEngineOptions { EnableContract = true };

        Assert.IsTrue(ValidationCategoryOrder.IsEnabled(options, ValidationRuleCategory.Contract));
    }

    [TestMethod]
    public void IsEnabled_null选项_应抛异常()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => ValidationCategoryOrder.IsEnabled(null!, ValidationRuleCategory.Structure));
    }

    [TestMethod]
    public void GetEnabled_应返回启用的分类()
    {
        var options = new ValidationEngineOptions { EnableContract = true };

        var enabled = ValidationCategoryOrder.GetEnabled(options).ToList();

        CollectionAssert.AreEqual(
            new List<ValidationRuleCategory>
            {
                ValidationRuleCategory.Structure,
                ValidationRuleCategory.Reference,
                ValidationRuleCategory.Semantic,
                ValidationRuleCategory.Contract
            },
            enabled);
    }

    [TestMethod]
    public void GetEnabled_全部禁用_应返回空()
    {
        var options = new ValidationEngineOptions
        {
            EnableStructure = false,
            EnableReference = false,
            EnableSemantic = false,
            EnableRuntime = false,
            EnableContract = false
        };

        var enabled = ValidationCategoryOrder.GetEnabled(options);

        Assert.AreEqual(0, enabled.Count);
    }

    [TestMethod]
    public void GetEnabled_null选项_应抛异常()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => ValidationCategoryOrder.GetEnabled(null!));
    }
}