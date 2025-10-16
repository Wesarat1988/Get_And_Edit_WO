using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Contracts;

namespace Hosting;
public static class PluginLoader
{
    public static IReadOnlyList<IPlugin> LoadAll(IServiceProvider services, string pluginsDir)
    {
        var list = new List<IPlugin>();
        if (!Directory.Exists(pluginsDir)) return list;

        foreach (var dir in Directory.GetDirectories(pluginsDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
            if (manifest is null) continue;

            var asmPath = Path.Combine(dir, manifest.Assembly);
            if (!File.Exists(asmPath)) continue;

            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(asmPath);
            var type = asm.GetType(manifest.EntryType, throwOnError: true)!;
            var plug = (Contracts.IPlugin)Activator.CreateInstance(type)!;

            plug.Initialize(services);
            list.Add(plug);
        }
        return list;
    }
}
