using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.Extensions.Logging;

namespace BlazorApp5.Hosting;

/// <summary>
/// Discovers and loads plugins described by <c>plugin.json</c> manifests.
/// </summary>
public static class PluginLoader
{
    private const string ManifestFileName = "plugin.json";
    private static readonly List<AssemblyLoadContext> _loadContexts = new();

    public static void LoadPlugins(
        string contentRootPath,
        IList<IBlazorPlugin> pluginStore,
        IServiceProvider serviceProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginStore);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var pluginsRoot = Path.Combine(contentRootPath, "Plugins");
        if (!Directory.Exists(pluginsRoot))
        {
            logger.LogInformation("Plugin directory '{PluginDirectory}' was not found. Skipping plugin discovery.", pluginsRoot);
            return;
        }

        foreach (var pluginDirectory in Directory.EnumerateDirectories(pluginsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                LoadPlugin(pluginDirectory, pluginStore, serviceProvider, logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load plugin from '{PluginDirectory}'.", pluginDirectory);
            }
        }
    }

    private static void LoadPlugin(
        string pluginDirectory,
        IList<IBlazorPlugin> pluginStore,
        IServiceProvider serviceProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("No plugin manifest found at '{ManifestPath}'.", manifestPath);
            return;
        }

        PluginManifest? manifest;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            manifest = JsonSerializer.Deserialize<PluginManifest>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException jsonEx)
        {
            logger.LogWarning(jsonEx, "Failed to parse manifest '{ManifestPath}'.", manifestPath);
            return;
        }

        string? validationError = manifest is null ? "Manifest could not be parsed." : null;
        if (manifest is null || !manifest.TryValidate(out validationError))
        {
            logger.LogWarning(
                "Invalid plugin manifest at '{ManifestPath}': {Error}",
                manifestPath,
                validationError ?? "Unknown error");
            return;
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, manifest.Assembly!));
        if (!File.Exists(assemblyPath))
        {
            logger.LogWarning("Assembly '{AssemblyPath}' for plugin '{PluginId}' was not found.", assemblyPath, manifest.Id);
            return;
        }

        var loadContext = new AssemblyLoadContext($"{manifest.Id}_{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var entryType = assembly.GetType(manifest.EntryType!, throwOnError: false, ignoreCase: false);
            if (entryType is null)
            {
                logger.LogWarning("Entry type '{EntryType}' could not be resolved in assembly '{AssemblyPath}'.", manifest.EntryType, assemblyPath);
                loadContext.Unload();
                return;
            }

            if (!typeof(IPlugin).IsAssignableFrom(entryType))
            {
                logger.LogWarning("Entry type '{EntryType}' does not implement IPlugin.", entryType.FullName);
                loadContext.Unload();
                return;
            }

            if (Activator.CreateInstance(entryType) is not IPlugin pluginInstance)
            {
                logger.LogWarning("Unable to create plugin instance for type '{EntryType}'.", entryType.FullName);
                loadContext.Unload();
                return;
            }

            pluginInstance.Initialize(serviceProvider);

            _ = Task.Run(async () =>
            {
                try
                {
                    await pluginInstance.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Normal during shutdown.
                }
                catch (Exception backgroundEx)
                {
                    logger.LogError(backgroundEx, "Background execution failed for plugin '{PluginId}'.", manifest.Id);
                }
            }, cancellationToken);

            if (pluginInstance is IBlazorPlugin blazorPlugin)
            {
                pluginStore.Add(blazorPlugin);
                logger.LogInformation("Loaded Blazor plugin '{PluginName}' ({PluginId}) version {PluginVersion}.", manifest.Name, manifest.Id, manifest.Version);
            }
            else
            {
                logger.LogInformation("Loaded headless plugin '{PluginName}' ({PluginId}) version {PluginVersion}.", manifest.Name, manifest.Id, manifest.Version);
            }

            _loadContexts.Add(loadContext);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }
}
