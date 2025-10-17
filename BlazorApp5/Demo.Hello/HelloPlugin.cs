using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Demo.Hello.Pages;
using Microsoft.Extensions.Logging;

namespace Demo.Hello;

public sealed class HelloPlugin : IBlazorPlugin
{
    private ILogger<HelloPlugin>? _logger;

    public string Id => "demo.hello";

    public string Name => "Hello Plugin";

    public Version Version => new(1, 0, 0);

    public Type? RootComponent => typeof(HelloPanel);

    public string? RouteBase => "/plugins/hello";

    public void Initialize(IServiceProvider services)
    {
        _logger = services.GetService(typeof(ILogger<HelloPlugin>)) as ILogger<HelloPlugin>;
        _logger?.LogInformation("HelloPlugin initialized");
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
