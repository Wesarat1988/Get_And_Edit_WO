using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorApp5;

public sealed class WorkOrderBlazorPlugin : IBlazorPlugin
{
    private ILogger<WorkOrderBlazorPlugin>? _logger;

    public string Id => "workorder";
    public string Name => "Work Order Plugin";
    public Version Version => new(1, 0, 0);
    public string? RouteBase => "/workorders";
    public Type RootComponent => typeof(Pages.WorkOrderPage);

    public void Initialize(IServiceProvider services)
    {
        _logger = services.GetService<ILogger<WorkOrderBlazorPlugin>>();
        _logger?.LogInformation("WorkOrder plugin initialized");
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger?.LogDebug("ExecuteAsync invoked with no background work to schedule.");
        return Task.CompletedTask;
    }
}
