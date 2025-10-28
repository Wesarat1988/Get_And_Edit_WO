using Contracts;

namespace GetAndEditWO.UI;

public sealed class WorkOrderBlazorPlugin : IBlazorPlugin
{
    public string Id => "workorder-plugin";
    public string Name => "Work Order";
    public Version Version => new(1, 0, 0, 0);

    public Type? RootComponent => typeof(Component1);
    public string? RouteBase => "/plugins/workorders";

    public void Initialize(IServiceProvider services)
    {
    }

    public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
}
