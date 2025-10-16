namespace Contracts;

using System;

public interface IBlazorPlugin : IPlugin
{
    Type? RootComponent { get; }
    string? RouteBase { get; }
}
