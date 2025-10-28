using System.Reflection;

namespace BlazorApp5.Hosting
{
    public sealed class PluginUiRegistry
    {
        public List<Assembly> Assemblies { get; } = new();
    }
}
