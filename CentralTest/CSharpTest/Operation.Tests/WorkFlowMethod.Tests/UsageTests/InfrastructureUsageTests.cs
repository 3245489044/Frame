using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Infrastructure;
using WorkFlowMethod.Infrastructure.Constants;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("Infrastructure")]
    public class InfrastructureUsageTests
    {
        [TestMethod]
        public void ExpandoObjectHelper_ShouldConvertAndQuery()
        {
            var dict = new Dictionary<string, object> { ["name"] = "test", ["value"] = 42 };
            dynamic expando = ExpandoObjectHelper.ConvertDictionaryToExpando(dict);
            Assert.AreEqual("test", expando.name);
            Assert.AreEqual(42, expando.value);

            var empty = ExpandoObjectHelper.ConvertDictionaryToExpando(new Dictionary<string, object>());
            Assert.AreEqual(0, ((IDictionary<string, object>)empty).Count);

            dynamic manual = new ExpandoObject();
            manual.Name = "Hello";
            Assert.AreEqual("Hello", ExpandoObjectHelper.GetStringValue(manual, "Name"));
            Assert.IsNull(ExpandoObjectHelper.GetStringValue(manual, "NonExistent"));
            Assert.IsNotNull(ExpandoObjectHelper.GetValue(manual, "Name"));
            Assert.IsNull(ExpandoObjectHelper.GetValue(manual, "Missing"));

            Assert.IsNotNull(ExpandoObjectHelper.CreateExpando());
        }

        [TestMethod]
        public void ValidationResult_ShouldCollectErrors()
        {
            var result = new ValidationResult();
            Assert.IsTrue(result.IsValid);

            result.AddError("错误1");
            result.AddError("错误2");
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(2, result.Errors.Count);
            Assert.IsTrue(result.GetErrorString().Contains("错误1"));

            Assert.IsTrue(ValidationResult.Success().IsValid);
            Assert.IsFalse(ValidationResult.Failure("致命错误").IsValid);
        }

        [TestMethod]
        public void WorkflowConstants_ShouldBeAccessible()
        {
            Assert.AreEqual("AI.ParentExecutionId", WorkflowConstants.ParentExecutionIdKey);
            Assert.AreEqual("AI.ExecutionId", WorkflowConstants.ExecutionIdKey);
            Assert.AreEqual("WF_", WorkflowConstants.ExecutionIdPrefix);
            Assert.AreEqual("工作流执行成功", WorkflowConstants.SuccessMessage);
            Assert.AreEqual(100, WorkflowConstants.NodeWaitTimeoutMilliseconds);
            Assert.IsTrue(WorkflowConstants.GlobalPauseEventInitialState);
        }

        [TestMethod]
        public void WorkflowDefaults_And_WorkflowStatus_ShouldBeDefined()
        {
            Assert.AreEqual(2, WorkflowDefaults.ImageWebShowChangeValue.Count);

            Assert.AreEqual(0, (int)WorkflowStatus.Initialized);
            Assert.AreEqual(2, (int)WorkflowStatus.Running);
            Assert.AreEqual(3, (int)WorkflowStatus.Completed);
            Assert.AreEqual(4, (int)WorkflowStatus.Error);
        }
    }
}
