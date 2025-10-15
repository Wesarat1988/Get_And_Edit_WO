using BlazorApp5;
using BlazorApp5.Services;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

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

// ================== Debug Endpoints (Dev only) ==================
if (app.Environment.IsDevelopment())
{
    // ยิงสัญญาณภายนอก: เขียน HR4096 = 1 → UI จะจับ polling เองและแสดงผล
    app.MapPost("/debug/raise-trigger", async (ModbusService svc) =>
    {
        var ok = await svc.RaiseExternalTriggerAsync(); // ⚠️ ต้องมีเมธอดนี้ใน ModbusService (ที่ผมให้ไว้ก่อนหน้า)
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
