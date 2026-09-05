using FunctionStorage;
using UniversalWorkflowFramework.FunctionExecution;
using WdUI.UI;
using BasicMethod.ConfigurationModels;
using System.Windows.Forms;

namespace FunctionStorage.Tests
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
        if (args.Length > 0 && args[0] == "--test")
        {
            var allPassed = true;

            try
            {
                Console.WriteLine("========================================");
                Console.WriteLine("  MathFunctionRegistrar 单元测试");
                Console.WriteLine("========================================\n");
                var registrarTests = new MathFunctionRegistrarTests();
                registrarTests.RegisterAll_ConfigNotFound_ShouldSkipGracefully();
                registrarTests.RegisterAll_EmptyConfig_ShouldSkipGracefully();
                registrarTests.RegisterAll_WithValidConfig_ShouldRegisterFunctions();
                registrarTests.RegisterAll_DuplicateCall_ShouldSkip();
                registrarTests.RegisteredFunctions_ShouldBeExecutable();
                Console.WriteLine("\n  MathFunctionRegistrar 测试通过！\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  MathFunctionRegistrar 测试失败: {ex.Message}\n");
                allPassed = false;
            }

            try
            {
                Console.WriteLine("========================================");
                Console.WriteLine("  MathBrowserFunctions 集成测试");
                Console.WriteLine("========================================\n");
                var browserTests = new MathBrowserFunctionsTests();
                browserTests.Math_GetDomains_AfterRegistration_ShouldReturnDomains();
                browserTests.Math_GetFunctionsInDomain_WithValidDomain_ShouldReturnFunctions();
                browserTests.Math_GetFunctionDetail_WithValidFunction_ShouldReturnDetail();
                browserTests.Math_Execute_WithValidParams_ShouldReturnResult();
                browserTests.Math_Search_WithKeyword_ShouldReturnMatches();
                browserTests.Math_GetStats_AfterRegistration_ShouldReturnStats();
                Console.WriteLine("\n  MathBrowserFunctions 测试通过！\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  MathBrowserFunctions 测试失败: {ex.Message}\n");
                allPassed = false;
            }

            try
            {
                var wfPassed = WorkflowIOGroupTests.RunAll();
                if (!wfPassed) allPassed = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  WorkflowIOGroup 测试异常: {ex.Message}\n");
                allPassed = false;
            }

            Console.WriteLine("========================================");
            Console.WriteLine(allPassed ? "  所有测试通过！" : "  存在失败的测试！");
            Console.WriteLine("========================================");

            Environment.Exit(allPassed ? 0 : 1);
            return;
        }

        ApplicationConfiguration.Initialize();

            WdUIComposer.Initialize();

            MathLibrary.RegisterAll(FunctionStorageRegistry.Registry);

            var config = CreateMathFunctionsConfig();

            var form = new Form
            {
                Name = "MathFunctions",
                Text = config.Form.Title,
                Width = config.Form.Width,
                Height = config.Form.Height,
                StartPosition = FormStartPosition.CenterScreen
            };

            foreach (var view in config.Views)
            {
                foreach (var ctrl in view.Controls)
                {
                    var control = WdUIComposer.Controls.CreateControl(ctrl, WdUIComposer.Mediator);
                    if (control != null) form.Controls.Add(control);
                }
            }

            var workflowNames = UIConnector.GetWorkflowNames();
            foreach (var name in workflowNames)
            {
                Console.WriteLine($"工作流: {name}");
            }

            Application.Run(form);
        }

        private static void RegisterBuiltInFunctions()
        {
            MathLibrary.RegisterAll(FunctionStorageRegistry.Registry);
            FunctionStorageRegistry.Register("Reverse", (Func<string, string>)(s => new string(s.Reverse().ToArray())), "字符串反转");
        }

        private static UIConfiguration CreateMathFunctionsConfig()
        {
            return new UIConfiguration
            {
                Form = new FormConfig
                {
                    Title = "Math Functions",
                    Width = 500,
                    Height = 420
                },
                Views = new List<ViewConfig>
                {
                    new ViewConfig
                    {
                        Name = "MainView",
                        Controls = new List<ControlConfig>
                        {
                            new ControlConfig { Type = "label", Name = "valueALabel", Text = "Value A:", X = 20, Y = 20, Width = 60 },
                            new ControlConfig { Type = "textbox", Name = "valueA", X = 85, Y = 18, Width = 120, Placeholder = "Enter number" },
                            
                            new ControlConfig { Type = "label", Name = "valueBLabel", Text = "Value B:", X = 220, Y = 20, Width = 60 },
                            new ControlConfig { Type = "textbox", Name = "valueB", X = 285, Y = 18, Width = 120, Placeholder = "Enter number" },
                            
                            new ControlConfig { Type = "button", Name = "addButton", Text = "Add (+)", X = 20, Y = 60, Width = 100,
                                Events = new EventConfig
                                {
                                    Click = new List<string> { "Add" },
                                    Bindings = new EventBinding
                                    {
                                        Inputs = new List<string> { "valueA", "valueB" },
                                        Output = "result"
                                    }
                                }
                            },
                            
                            new ControlConfig { Type = "button", Name = "subButton", Text = "Sub (-)", X = 130, Y = 60, Width = 100,
                                Events = new EventConfig
                                {
                                    Click = new List<string> { "Subtract" },
                                    Bindings = new EventBinding
                                    {
                                        Inputs = new List<string> { "valueA", "valueB" },
                                        Output = "result"
                                    }
                                }
                            },
                            
                            new ControlConfig { Type = "button", Name = "mulButton", Text = "Mul (*)", X = 240, Y = 60, Width = 100,
                                Events = new EventConfig
                                {
                                    Click = new List<string> { "Multiply" },
                                    Bindings = new EventBinding
                                    {
                                        Inputs = new List<string> { "valueA", "valueB" },
                                        Output = "result"
                                    }
                                }
                            },
                            
                            new ControlConfig { Type = "button", Name = "divButton", Text = "Div (/)", X = 350, Y = 60, Width = 100,
                                Events = new EventConfig
                                {
                                    Click = new List<string> { "Divide" },
                                    Bindings = new EventBinding
                                    {
                                        Inputs = new List<string> { "valueA", "valueB" },
                                        Output = "result"
                                    }
                                }
                            },

                            
                            new ControlConfig { Type = "label", Name = "resultLabel", Text = "Result:", X = 20, Y = 110, Width = 60 },
                            new ControlConfig { Type = "textbox", Name = "result", X = 85, Y = 108, Width = 365, ReadOnly = true },
                            
                            new ControlConfig { Type = "label", Name = "stringLabel", Text = "String:", X = 20, Y = 160, Width = 60 },
                            new ControlConfig { Type = "textbox", Name = "inputString", X = 85, Y = 158, Width = 200, Placeholder = "Enter text" },
                            
                            new ControlConfig { Type = "button", Name = "reverseButton", Text = "Reverse", X = 295, Y = 156, Width = 155,
                                Events = new EventConfig
                                {
                                    Click = new List<string> { "Reverse" },
                                    Bindings = new EventBinding
                                    {
                                        Inputs = new List<string> { "inputString" },
                                        Output = "inputString"
                                    }
                                }
                            }

                        }
                    }
                }
            };
        }
    }
}