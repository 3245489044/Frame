using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Abstractions;
using WorkFlowMethod.LoopFramework.Core;
using WorkFlowMethod.LoopFramework.StateMachine;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("LoopFramework")]
    public class LoopFrameworkUsageTests
    {
        [TestMethod]
        public void LoopState_Values_ShouldBeDefined()
        {
            Assert.AreEqual(0, (int)LoopState.NotStarted);
            Assert.AreEqual(1, (int)LoopState.Running);
            Assert.AreEqual(4, (int)LoopState.Completed);
            Assert.AreEqual(7, (int)LoopState.MaxIterationsReached);
        }

        [TestMethod]
        public void ActionResult_DefaultAndCustom_ShouldWork()
        {
            var def = new ActionResult();
            Assert.IsFalse(def.Success);
            Assert.AreEqual(0, def.Outputs.Count);

            var custom = new ActionResult { Success = false, Error = "error", Duration = TimeSpan.FromSeconds(1) };
            custom.Outputs["key"] = "value";
            Assert.IsFalse(custom.Success);
            Assert.AreEqual("value", custom.Outputs["key"]);
        }

        [TestMethod]
        public void StateTransition_DefaultAndCompleted_ShouldWork()
        {
            var def = new StateTransition<LoopState>();
            Assert.IsFalse(def.IsCompleted);
            Assert.IsNull(def.ActionName);

            var completed = new StateTransition<LoopState> { IsCompleted = true, NextState = LoopState.Completed, ActionName = "Complete", Reasoning = "done" };
            Assert.IsTrue(completed.IsCompleted);
            Assert.AreEqual(LoopState.Completed, completed.NextState);
        }

    }

}
