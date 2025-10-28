using System;
using System.Reflection;
using System.Runtime.Loader;

namespace BlazorApp5.Hosting;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginMainDllPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginMainDllPath);

        // เผื่อไว้: ถ้าหาไม่เจอเรียก Resolving ช่วย
        Resolving += (ctx, name) =>
            name.Name == "Contracts" ? Assembly.Load(name) : null;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 👉 ยืม Contracts จาก Default load context (Host โหลดไว้แล้ว)
        if (assemblyName.Name == "Contracts")
            return Assembly.Load(assemblyName);

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
