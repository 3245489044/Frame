﻿#region USINGS
using System;
using System.Collections.Generic;
using Modules.StateMachine;
using Share.StateManagement;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

using SMIPauseManager = Modules.StateMachine.IPauseManager;
using ExecutionStatus = Modules.StateMachine.ExecutionStatus;

namespace WorkFlowMethod.Orchestration.State
{
    public class StateManager
    {
        public const int DefaultMaxConcurrency = 10;

        public SMIPauseManager PauseManager { get; set; }

        public IStateMachineStore<ExecutionStatus> ExecutionStateStore { get; set; }

        public INodeExecutionCoordinator NodeExecutionCoordinator { get; set; }

        public Share.StateManagement.IStateStore StageStateStore { get; set; }

        public StateManager(int maxConcurrency = DefaultMaxConcurrency)
        {
            PauseManager = new Modules.StateMachine.PauseManager();
            ExecutionStateStore = new StateMachineStore<ExecutionStatus>();
            NodeExecutionCoordinator = new NodeExecutionCoordinator(
                maxConcurrency: maxConcurrency);
            StageStateStore = new Share.StateManagement.InMemoryStateStore();
        }

        public static StateManager CreateDefault()
        {
            var manager = new StateManager();
            return manager;
        }

        public void Dispose()
        {
            (NodeExecutionCoordinator as IDisposable)?.Dispose();
            (PauseManager as IDisposable)?.Dispose();
        }
    }
}

