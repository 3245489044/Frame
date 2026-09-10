
using System;

namespace FrameworkAbstractions.Bootstrapping
{
    public interface IBootstrapperContext
    {
        bool CliMode { get; }

        string? ProjectRoot { get; }

        IServiceProvider? Services { get; }
    }

    public class BootstrapperContext : IBootstrapperContext
    {
        public bool CliMode { get; set; }

        public string? ProjectRoot { get; set; }

        public IServiceProvider? Services { get; set; }

        public BootstrapperContext() { }

        public BootstrapperContext(bool cliMode, string? projectRoot = null, IServiceProvider? services = null)
        {
            CliMode = cliMode;
            ProjectRoot = projectRoot;
            Services = services;
        }
    }
}
