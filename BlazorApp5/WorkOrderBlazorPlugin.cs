using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace BlazorApp5;

public sealed class WorkOrderBlazorPlugin : IBlazorPlugin
{
    public string Id => "workorder";
    public string Name => "Work Order Plugin";
    public Version Version => new(1, 0, 0);
    public string RouteBase => "/workorders";
    public Type RootComponent => typeof(Pages.WorkOrderPage);

    public void Initialize(IServiceProvider services)
    {
        // ลงทะเบียน service ของปลั๊กอิน หรือล็อกข้อความ ฯลฯ (ถ้าต้องใช้)
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
