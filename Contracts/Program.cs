using Contracts;
using Hosting;

builder.Services.AddSingleton<List<IBlazorPlugin>>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var pluginsDir = Path.Combine(app.Environment.ContentRootPath, "Plugins");
    var all = PluginLoader.LoadAll(services, pluginsDir);

    var uiBucket = services.GetRequiredService<List<IBlazorPlugin>>();
    foreach (var p in all)
    {
        _ = p.ExecuteAsync();
        if (p is IBlazorPlugin ui) uiBucket.Add(ui);
    }
}

app.Run();
