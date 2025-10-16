using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp5.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.CSharp;
using Microsoft.Extensions.Logging;

namespace BlazorApp5.Hosting;

/// <summary>
/// Generates a small demo plugin that showcases the loader in environments where a pre-built DLL is not provided.
/// </summary>
public static class SamplePluginPublisher
{
    private const string SamplePluginId = "hello";
    private const string SampleAssemblyName = "HelloPlugin.dll";

    public static void EnsureSamplePlugin(string contentRootPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(contentRootPath);
        ArgumentNullException.ThrowIfNull(logger);

        var pluginDirectory = Path.Combine(contentRootPath, "Plugins", "Hello");
        Directory.CreateDirectory(pluginDirectory);

        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            var manifest = new
            {
                id = SamplePluginId,
                name = "Hello Plugin",
                version = "1.0.0",
                assembly = SampleAssemblyName,
                entryType = "HelloPlugin.HelloBlazorPlugin"
            };

            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);
        }

        var assemblyPath = Path.Combine(pluginDirectory, SampleAssemblyName);
        if (File.Exists(assemblyPath))
        {
            return;
        }

        try
        {
            CompileSamplePlugin(assemblyPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate the sample plugin assembly.");
            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }
        }
    }

    private static void CompileSamplePlugin(string assemblyPath, ILogger logger)
    {
        using var provider = new CSharpCodeProvider();
        var parameters = new CompilerParameters
        {
            GenerateExecutable = false,
            GenerateInMemory = false,
            OutputAssembly = assemblyPath,
            IncludeDebugInformation = false,
            CompilerOptions = "/target:library"
        };

        var referencedAssemblies = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Task).Assembly.Location,
            typeof(ComponentBase).Assembly.Location,
            typeof(RenderTreeBuilder).Assembly.Location,
            typeof(IPlugin).Assembly.Location,
        };

        foreach (var reference in referencedAssemblies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            parameters.ReferencedAssemblies.Add(reference);
        }

        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using BlazorApp5.Contracts;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;

            namespace HelloPlugin;

            public sealed class HelloBlazorPlugin : IBlazorPlugin
            {
                public string Id => "hello";
                public string Name => "Hello Plugin";
                public string Version => "1.0.0";
                public Type? RootComponent => typeof(HelloPluginComponent);
                public string? RouteBase => "hello";

                public void Initialize(IServiceProvider serviceProvider)
                {
                }

                public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class HelloPluginComponent : ComponentBase
            {
                protected override void BuildRenderTree(RenderTreeBuilder builder)
                {
                    builder.OpenElement(0, "div");
                    builder.AddAttribute(1, "class", "alert alert-info");
                    builder.AddContent(2, "Hello from the dynamically generated plugin!");
                    builder.CloseElement();
                }
            }
            """;

        var results = provider.CompileAssemblyFromSource(parameters, source);
        if (results.Errors.HasErrors)
        {
            var errors = string.Join(Environment.NewLine, results.Errors.Cast<CompilerError>());
            throw new InvalidOperationException($"Sample plugin compilation failed:{Environment.NewLine}{errors}");
        }

        logger.LogInformation("Sample plugin assembly generated at '{AssemblyPath}'.", assemblyPath);
    }
}
