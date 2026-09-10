
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;

global using CommandFramework.Abstractions;

global using TConfig;

global using Entity = TConfig.ECS.Entity;
global using IComponent = TConfig.ECS.IComponent;

global using BindingResult = CommandFramework.Abstractions.BindingResult;
global using IEventBindingResolver = TConfig.IEventBindingResolver;
global using IParameterExecutionWorkflow = TConfig.IParameterExecutionWorkflow;
global using CommandCriticality = CommandFramework.Abstractions.CommandCriticality;

global using ContainerExecutionResult = Container.Abstractions.ExecutionResult;

global using IControlFinder = FrameworkAbstractions.WinFormsAbstractions.IControlFinder;

global using INonEventCommandLookup = Container.Abstractions.INonEventCommandLookup;


