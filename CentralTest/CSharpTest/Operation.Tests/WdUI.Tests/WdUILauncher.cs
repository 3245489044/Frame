
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WdUI;
using WdUI.UI.Controls;
using WdUI.UI;
using WdUI.UI.Commands;
using WdUI.Layout;
using WdUI.Serialization;
using WdUI.Settings;
using TestInfrastructure;

namespace WdUI.Tests
{
    [SupportedOSPlatform("windows")]
    internal static class WdUILauncher
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            MessagePumpEngine.Instance.Initialize();

            var form = MessagePumpEngine.Instance.CreateForm(900, 700);
            form.Text = "WdUI 集成测试窗口";
            form.Name = "WdUIIntegrationForm";
            form.StartPosition = FormStartPosition.CenterScreen;

            var tabControl = new TabControl { Dock = DockStyle.Fill };
            form.Controls.Add(tabControl);

            var tab1 = new TabPage("窗口与容器");
            tabControl.TabPages.Add(tab1);

            var infoLabel1 = new Label
            {
                Text = "WdUI 集成测试窗口\n\n本窗口展示了 WdUI 框架的核心功能。\n每个 Tab 页展示一个功能模块。",
                Location = new Point(10, 10), Size = new Size(400, 80),
                Font = new Font("Segoe UI", 10)
            };
            tab1.Controls.Add(infoLabel1);

            var jsonConfig = CreateTestJsonConfig();
            var config = WdUIUsage.DeserializeConfig(jsonConfig);
            if (config != null)
            {
                var container = new ConfigContainer
                {
                    Name = "demoContainer",
                    Location = new Point(10, 100), Size = new Size(400, 250),
                    BorderStyle = BorderStyle.FixedSingle
                };
                tab1.Controls.Add(container);
                try { WdUIUsage.RenderAsync(container, config).Wait(TimeSpan.FromSeconds(5)); }
                catch { }
            }

            var tab2 = new TabPage("控件工厂");
            tabControl.TabPages.Add(tab2);
            var factoryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            tab2.Controls.Add(factoryPanel);

            void AddCtrl(string type, string text)
            {
                var c = WdUIUsage.CreateControl(type);
                if (c != null) { c.Text = text; c.Size = new Size(180, 30); factoryPanel.Controls.Add(c); }
            }
            AddCtrl("button", "Button 控件");
            AddCtrl("textbox", "TextBox 控件");
            AddCtrl("label", "Label 控件");
            AddCtrl("panel", "Panel 控件");

            WdUIUsage.RegisterControlType("customBtn", () => new Button { Text = "自定义按钮", BackColor = Color.LightBlue, Size = new Size(180, 30) });
            var customBtn = WdUIUsage.CreateControl("customBtn");
            if (customBtn != null) factoryPanel.Controls.Add(customBtn);

            var tab3 = new TabPage("命令引擎");
            tabControl.TabPages.Add(tab3);
            var cmdLog = new ListBox { Location = new Point(10, 10), Size = new Size(400, 200) };
            tab3.Controls.Add(cmdLog);

            WdUIUsage.RegisterCommand("demo.sayHello", async (ctx, ct) =>
            { cmdLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] 执行: sayHello"); await Task.CompletedTask; });
            WdUIUsage.RegisterCommand("demo.greet", async (ctx, ct) =>
            { var n = ctx.Parameters?.TryGetValue("name", out var v) == true ? v : "World"; cmdLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] 你好, {n}!"); await Task.CompletedTask; });

            var btnHello = new Button { Text = "执行 sayHello", Location = new Point(10, 220), Size = new Size(120, 30) };
            btnHello.Click += async (s, e) => { await WdUIUsage.ExecuteCommandAsync("demo.sayHello"); };
            tab3.Controls.Add(btnHello);

            var btnGreet = new Button { Text = "执行 greet(World)", Location = new Point(140, 220), Size = new Size(150, 30) };
            btnGreet.Click += async (s, e) => { await WdUIUsage.ExecuteCommandAsync("demo.greet", new Dictionary<string, object?> { ["name"] = "World" }); };
            tab3.Controls.Add(btnGreet);

            var tab4 = new TabPage("数据绑定");
            tabControl.TabPages.Add(tab4);
            var dataSource = new BindablePerson { Name = "张三", Age = 25 };
            WdUIUsage.RegisterDataSource("person", dataSource);

            tab4.Controls.Add(new Label { Text = "姓名:", Location = new Point(10, 10), Size = new Size(60, 30) });
            var txtName = new TextBox { Location = new Point(80, 10), Size = new Size(150, 30) };
            tab4.Controls.Add(txtName); WdUIUsage.BindControl(txtName, "person", "Name");

            tab4.Controls.Add(new Label { Text = "年龄:", Location = new Point(10, 50), Size = new Size(60, 30) });
            var txtAge = new TextBox { Location = new Point(80, 50), Size = new Size(150, 30) };
            tab4.Controls.Add(txtAge); WdUIUsage.BindControl(txtAge, "person", "Age");

            var btnUpdate = new Button { Text = "更新数据源", Location = new Point(10, 90), Size = new Size(120, 30) };
            btnUpdate.Click += (s, e) => { WdUIUsage.UpdateDataSource("person", new BindablePerson { Name = $"李四 ({DateTime.Now:HH:mm:ss})", Age = new Random().Next(18, 60) }); };
            tab4.Controls.Add(btnUpdate);

            var btnGet = new Button { Text = "读取数据源", Location = new Point(140, 90), Size = new Size(120, 30) };
            btnGet.Click += (s, e) => { var p = WdUIUsage.GetDataSource<BindablePerson>("person"); MessageBox.Show($"姓名: {p?.Name}, 年龄: {p?.Age}", "数据源内容"); };
            tab4.Controls.Add(btnGet);

            var tab5 = new TabPage("事件总线");
            tabControl.TabPages.Add(tab5);
            var eventLog = new ListBox { Location = new Point(10, 10), Size = new Size(400, 180) };
            tab5.Controls.Add(eventLog);

            WdUIUsage.SubscribeEvent("demo.event", async data => { eventLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] 收到事件: {data}"); await Task.CompletedTask; });

            var btnPub1 = new Button { Text = "发布事件(无数据)", Location = new Point(10, 200), Size = new Size(140, 30) };
            btnPub1.Click += async (s, e) => { await WdUIUsage.PublishEventAsync("demo.event"); };
            tab5.Controls.Add(btnPub1);

            var btnPub2 = new Button { Text = "发布事件(带数据)", Location = new Point(160, 200), Size = new Size(150, 30) };
            btnPub2.Click += async (s, e) => { await WdUIUsage.PublishEventAsync("demo.event", $"按钮点击 @ {DateTime.Now:HH:mm:ss}"); };
            tab5.Controls.Add(btnPub2);

            var tab6 = new TabPage("布局与查找");
            tabControl.TabPages.Add(tab6);
            var layoutPanel = new Panel { Name = "layoutDemo", Location = new Point(10, 10), Size = new Size(400, 200), BorderStyle = BorderStyle.FixedSingle };
            tab6.Controls.Add(layoutPanel);
            layoutPanel.Controls.AddRange(new Control[] {
                new Button { Name = "btnA", Text = "按钮 A", Location = new Point(10, 10), Size = new Size(80, 30) },
                new Button { Name = "btnB", Text = "按钮 B", Location = new Point(100, 10), Size = new Size(80, 30) },
                new Button { Name = "btnC", Text = "按钮 C", Location = new Point(10, 50), Size = new Size(80, 30) }
            });

            var btnFind = new Button { Text = "查找按钮 B", Location = new Point(10, 220), Size = new Size(120, 30) };
            btnFind.Click += (s, e) => { var f = WdUIUsage.FindControl(layoutPanel, "btnB"); MessageBox.Show(f != null ? $"找到: {f.Name}" : "未找到", "查找结果"); };
            tab6.Controls.Add(btnFind);

            var btnFindType = new Button { Text = "查找所有 Button", Location = new Point(140, 220), Size = new Size(140, 30) };
            btnFindType.Click += (s, e) => { var btns = WdUIUsage.FindAllControlsByType<Button>(layoutPanel); MessageBox.Show($"找到 {btns.Count} 个 Button", "查找结果"); };
            tab6.Controls.Add(btnFindType);

            var btnOverlap = new Button { Text = "检测重叠", Location = new Point(290, 220), Size = new Size(100, 30) };
            btnOverlap.Click += (s, e) => { WdUIUsage.DetectOverlaps(layoutPanel); MessageBox.Show("重叠检测完成", "检测结果"); };
            tab6.Controls.Add(btnOverlap);

            var tab7 = new TabPage("设置与 JSON");
            tabControl.TabPages.Add(tab7);
            var settingsInfo = new TextBox { Multiline = true, Location = new Point(10, 10), Size = new Size(400, 200), Font = new Font("Consolas", 9), ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            tab7.Controls.Add(settingsInfo);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== WdUI 设置信息 ===");
            sb.AppendLine($"默认字体: {WdUIUsage.Settings.DefaultFont.ToFont()?.Name} {WdUIUsage.Settings.DefaultFont.ToFont()?.Size}pt");
            sb.AppendLine($"渲染管线: {WdUIUsage.Settings.Pipelines.RenderPipeline}");
            sb.AppendLine($"配置加载器: {WdUIUsage.Settings.Pipelines.ConfigLoader}");
            sb.AppendLine($"ConfigLoaded 事件: {WdUIUsage.Settings.EventNames.ConfigLoaded}");
            sb.AppendLine($"ContentChanged 事件: {WdUIUsage.Settings.EventNames.ContentChanged}");
            sb.AppendLine();
            sb.AppendLine("已注册控件类型:");
            foreach (var t in WdUIUsage.Settings.ControlTypes.GetRegisteredControlTypes())
                sb.AppendLine($"  - {t}");
            sb.AppendLine();
            sb.AppendLine("=== JSON 配置 ===");
            sb.AppendLine(CreateTestJsonConfig());
            settingsInfo.Text = sb.ToString();

            var statusBar = new StatusStrip();
            statusBar.Items.Add(new ToolStripStatusLabel("WdUI 集成测试窗口已启动"));
            form.Controls.Add(statusBar);

            WdUIUsage.RegisterForm(form);
            form.Show();
            Application.Run(form);
        }

        private static string CreateTestJsonConfig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"form\":{\"title\":\"测试表单\",\"width\":400,\"height\":300},");
            sb.Append("\"views\":[{\"name\":\"mainView\",\"controls\":[");
            sb.Append("{\"name\":\"lblTitle\",\"type\":\"label\",\"text\":\"标题标签\",\"x\":10,\"y\":10,\"width\":100,\"height\":30},");
            sb.Append("{\"name\":\"txtName\",\"type\":\"textbox\",\"text\":\"输入框\",\"x\":10,\"y\":50,\"width\":200,\"height\":30},");
            sb.Append("{\"name\":\"btnSubmit\",\"type\":\"button\",\"text\":\"提交按钮\",\"x\":10,\"y\":100,\"width\":80,\"height\":30}");
            sb.Append("]}]}");
            return sb.ToString();
        }
    }

    internal class BindablePerson : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _age;
        public string Name { get => _name; set { if (_name != value) { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } } }
        public int Age { get => _age; set { if (_age != value) { _age = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age))); } } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
