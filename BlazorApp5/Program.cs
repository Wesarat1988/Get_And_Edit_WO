using BlazorApp5;
using BlazorApp5.Hosting;
using BlazorApp5.Services;
using Contracts;
using System.IO;
using System.Linq;
using System.Reflection; // ✅ ใช้ดูชื่อ Assembly
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Prefer stable development ports when none are specified explicitly.
if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls("https://localhost:5001", "http://localhost:5000");
}

// --- Services ---
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.Configure<ModbusOptions>(builder.Configuration.GetSection("Modbus"));

builder.Services.AddSingleton<MesService>();
builder.Services.AddScoped<RoutingCheckService>();

// ✅ ใช้ Singleton เพื่อแชร์การเชื่อมต่อ/คิว Modbus ทั้งแอป (UI + Debug endpoint)
builder.Services.AddSingleton<ModbusService>();

// ✅ ที่เก็บอินสแตนซ์ปลั๊กอิน และรีจิสทรี Assembly สำหรับ Router
builder.Services.AddSingleton<List<IBlazorPlugin>>();
builder.Services.AddSingleton<IReadOnlyList<IBlazorPlugin>>(sp => sp.GetRequiredService<List<IBlazorPlugin>>());
builder.Services.AddSingleton<PluginUiRegistry>();

var app = builder.Build();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var pluginLogger = loggerFactory.CreateLogger("PluginLoader");

// (ออปชัน) สร้าง/ก็อปตัวอย่างปลั๊กอินลงโฟลเดอร์ Plugins ถ้ายังไม่มี
SamplePluginPublisher.EnsureSamplePlugin(app.Environment.ContentRootPath, pluginLogger);

// โหลดปลั๊กอินเข้า store
var pluginStore = app.Services.GetRequiredService<List<IBlazorPlugin>>();
PluginLoader.LoadPlugins(app.Environment.ContentRootPath, pluginStore, app.Services, pluginLogger);

// ✅ ผูก UI assemblies ของปลั๊กอินให้ Router เห็น (ผ่านรีจิสทรี)
var uiRegistry = app.Services.GetRequiredService<PluginUiRegistry>();
foreach (var p in pluginStore)
{
    if (p.RootComponent is not null)
    {
        var asm = p.RootComponent.Assembly;
        if (!uiRegistry.Assemblies.Contains(asm))
            uiRegistry.Assemblies.Add(asm);
    }
}

// --- Pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection(); // ใช้เฉพาะ Production
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// ===== Tools / Debug =====
app.MapGet("/_assets-check", (IWebHostEnvironment env) =>
{
    var root = env.WebRootPath;

    string[] bootstrapCssCandidates =
    {
        "bootstrap/dist/css/bootstrap.min.css",
        "bootstrap/css/bootstrap.min.css"
    };

    string[] bootstrapJsCandidates =
    {
        "bootstrap/dist/js/bootstrap.bundle.min.js",
        "bootstrap/js/bootstrap.bundle.min.js"
    };

    string[] appCssCandidates =
    {
        "app.css",
        "tools/app.css"
    };

    string? ResolveFirstExisting(string[] candidates) =>
        candidates.FirstOrDefault(path => File.Exists(Path.Combine(root, path)));

    var resolvedBootstrapCss = ResolveFirstExisting(bootstrapCssCandidates);
    var resolvedBootstrapJs = ResolveFirstExisting(bootstrapJsCandidates);
    var resolvedAppCss = ResolveFirstExisting(appCssCandidates);

    var requiredFiles = new[]
    {
        resolvedBootstrapCss,
        resolvedBootstrapJs,
        resolvedAppCss,
        "css/styles.css",
        "css/camera-ui.css",
        "js/localStorageHelper.js",
        "js/downloadHelper.js",
        "js/controller.js",
        "js/scripts.js",
        "favicon.png"
    }
    .Where(path => !string.IsNullOrEmpty(path))
    .Cast<string>()
    .ToDictionary(path => path, path => File.Exists(Path.Combine(root, path)));

    var candidateStatus = new
    {
        bootstrapCss = bootstrapCssCandidates.ToDictionary(p => p, p => File.Exists(Path.Combine(root, p))),
        bootstrapJs = bootstrapJsCandidates.ToDictionary(p => p, p => File.Exists(Path.Combine(root, p))),
        appCss = appCssCandidates.ToDictionary(p => p, p => File.Exists(Path.Combine(root, p)))
    };

    return Results.Json(new
    {
        webRoot = root,
        resolved = new
        {
            bootstrapCss = resolvedBootstrapCss,
            bootstrapJs = resolvedBootstrapJs,
            appCss = resolvedAppCss
        },
        required = requiredFiles,
        candidates = candidateStatus
    });
});

// ✅ เอ็นด์พอยต์ตรวจว่าปลั๊กอิน/แอสเซมบลี Router ลงทะเบียนแล้วหรือยัง
app.MapGet("/_plugins", (IReadOnlyList<IBlazorPlugin> plugins, PluginUiRegistry ui) =>
{
    return Results.Json(new
    {
        plugins = plugins.Select(p => new
        {
            p.Id,
            p.Name,
            Version = p.Version.ToString(),
            RouteBase = (p as Contracts.IBlazorPlugin)?.RouteBase
        }),
        uiAssemblies = ui.Assemblies.Select(a => a.GetName().Name).ToArray()
    });
});

// ================== Debug Endpoints (Dev only) ==================
if (app.Environment.IsDevelopment())
{
    // ยิงสัญญาณภายนอก: เขียน HR4096 = 1 → UI จะจับ polling เองและแสดงผล
    app.MapPost("/debug/raise-trigger", async (ModbusService svc) =>
    {
        var ok = await svc.RaiseExternalTriggerAsync();
        return ok ? Results.Ok(new { ok = true, ts = DateTime.Now }) : Results.Problem("failed");
    });

    // เช็คสถานะการเชื่อมต่อแบบเร็ว (TCP อย่างเดียว)
    app.MapGet("/debug/status", async (ModbusService svc) =>
    {
        var connected = await svc.CheckConnectionAsync(tcpOnly: true);
        return Results.Ok(new { connected });
    });
}
// ===============================================================

app.Run();
