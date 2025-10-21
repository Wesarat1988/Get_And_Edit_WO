using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkOrderBlazorPlugin;

/// <summary>
/// Entry point for the Work Orders Blazor plugin.
/// </summary>
public sealed class PluginEntry : IBlazorPlugin
{
    public const string PluginId = "workorder";
    public const string PluginName = "Work Orders";
    public const string WorkOrdersRoute = "/workorders";

    private ILogger<PluginEntry>? _logger;

    public string Id => PluginId;

    public string Name => PluginName;

    public Version Version => new(1, 0, 0);

    public string? BaseRoute => WorkOrdersRoute;

    public Type? RootComponent => typeof(Pages.WorkOrderList);

    /// <summary>
    /// Allows the host to register any plugin services prior to activation.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    public void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _logger = services.GetService<ILogger<PluginEntry>>();
        _logger?.LogInformation("Work Orders plugin initialized.");
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        _logger?.LogDebug("Work Orders plugin has no background workload to execute.");
        return Task.CompletedTask;
    }
}
