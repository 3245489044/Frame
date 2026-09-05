using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlow.Config;
using Newtonsoft.Json.Linq;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("WorkFlowsSet")]
    public class WorkFlowsSetUsageTests
    {
        #region 13.1 WorkFlowsSet

        [TestMethod]
        public void BaseVersion_JToken_ShouldHaveConfigs()
        {
            var jtokenConfigs = WorkFlowsSet.BaseVersion.baseVersion_JToken;
            Assert.IsNotNull(jtokenConfigs);
            Assert.IsTrue(jtokenConfigs.Count > 0);
        }

        [TestMethod]
        public void BaseVersion_String_ShouldHaveConfigs()
        {
            var stringConfigs = WorkFlowsSet.BaseVersion.baseVersion_String;
            Assert.IsNotNull(stringConfigs);
            Assert.IsTrue(stringConfigs.Count > 0);
        }

        [TestMethod]
        public void BaseVersion_ConnectVersion_ShouldHaveConfigs()
        {
            var connectConfigs = WorkFlowsSet.BaseVersion.connectVersion;
            Assert.IsNotNull(connectConfigs);
        }

        [TestMethod]
        public void BaseVersion_ConnectRuleVersion_ShouldHaveConfigs()
        {
            var ruleConfigs = WorkFlowsSet.BaseVersion.connectRuleVersion;
            Assert.IsNotNull(ruleConfigs);
        }

        [TestMethod]
        public void BaseVersion_PredefinedWorkflows_ShouldBeAccessible()
        {
            string imageGen = WorkFlowsSet.BaseVersion.图像生成;
            string llmCall = WorkFlowsSet.BaseVersion.大模型调用生成;
            string codeGen = WorkFlowsSet.BaseVersion.代码综合生成;
            string codeMix = WorkFlowsSet.BaseVersion.代码混合生成;
            string compileTest = WorkFlowsSet.BaseVersion.编译测试;
            string dataStorage = WorkFlowsSet.BaseVersion.数据包装存储;

            Assert.IsFalse(string.IsNullOrEmpty(imageGen));
            Assert.IsFalse(string.IsNullOrEmpty(llmCall));
            Assert.IsFalse(string.IsNullOrEmpty(codeGen));
            Assert.IsFalse(string.IsNullOrEmpty(codeMix));
            Assert.IsFalse(string.IsNullOrEmpty(compileTest));
            Assert.IsFalse(string.IsNullOrEmpty(dataStorage));
        }

        [TestMethod]
        public void BlockStorage_ShouldBeAccessible()
        {
            var dataAnalysisBlock = WorkFlowsSet.BlockStorage.数据分析;
            Assert.IsNotNull(dataAnalysisBlock);
            Assert.IsFalse(string.IsNullOrEmpty(dataAnalysisBlock.Value));
            Assert.IsFalse(string.IsNullOrEmpty(dataAnalysisBlock.Stream));

            var codeGenBlock = WorkFlowsSet.BlockStorage.代码生成;
            Assert.IsNotNull(codeGenBlock);
        }

        #endregion
    }
}
