namespace Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    void Initialize(IServiceProvider services);
    Task ExecuteAsync(CancellationToken ct = default);
}
