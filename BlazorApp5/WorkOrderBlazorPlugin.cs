using Contracts;

namespace BlazorApp5; // ← ปรับตาม namespace จริงของปลั๊กอินคุณ

public sealed class WorkOrderBlazorPlugin : IBlazorPlugin
{
    public string Id => "workorder";
    public string Name => "Work Order Plugin";
    public Version Version => new(1, 0, 0);

    // ชี้ให้ Host รู้ว่าจะเรนเดอร์คอมโพเนนต์ไหน
    public Type? RootComponent => typeof(Pages.WorkOrderPanel);

    // ถ้าจะมีเพจของปลั๊กอินเอง ใช้ path นี้ (ยังไม่จำเป็น)
    public string? RouteBase => "/plugins/workorder";

    public void Initialize(IServiceProvider services)
    {
        // ลงทะเบียน service ของปลั๊กอิน หรือล็อกข้อความ ฯลฯ (ถ้าต้องใช้)
        // var logger = services.GetService<ILogger<WorkOrderBlazorPlugin>>();
        // logger?.LogInformation("WorkOrder plugin initialized");
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        // งาน background ถ้ามี (ถ้าไม่มีก็ปล่อยว่าง)
        return Task.CompletedTask;
    }
}
