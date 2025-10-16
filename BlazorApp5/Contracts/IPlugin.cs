using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorApp5.Contracts;

/// <summary>
/// Represents the minimal contract every plugin must satisfy.
/// </summary>
public interface IPlugin
{
    string Id { get; }

    string Name { get; }

    string Version { get; }

    /// <summary>
    /// Provides the plugin with access to application services.
    /// </summary>
    /// <param name="serviceProvider">The root <see cref="IServiceProvider"/> for the host application.</param>
    void Initialize(IServiceProvider serviceProvider);

    /// <summary>
    /// Allows a plugin to perform optional background work after initialization.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the execution.</param>
    /// <returns>A task representing the asynchronous work.</returns>
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Extends <see cref="IPlugin"/> with Blazor specific metadata that enables UI rendering.
/// </summary>
public interface IBlazorPlugin : IPlugin
{
    /// <summary>
    /// Gets the root component that should be rendered for this plugin, if any.
    /// </summary>
    Type? RootComponent { get; }

    /// <summary>
    /// Gets the optional base route the plugin wishes to expose.
    /// </summary>
    string? RouteBase { get; }
}
