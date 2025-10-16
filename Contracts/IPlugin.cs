// IPlugin.cs
namespace Contracts;
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    void Initialize(IServiceProvider services);
    Task ExecuteAsync(CancellationToken ct = default);
}