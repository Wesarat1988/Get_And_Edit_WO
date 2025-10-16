// IBlazorPlugin.cs
namespace Contracts;
public interface IBlazorPlugin : IPlugin
{
    Type? RootComponent { get; }
    string? RouteBase { get; }
}
