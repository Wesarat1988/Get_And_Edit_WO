using Microsoft.AspNetCore.Components;
namespace Contracts;
public interface IBlazorPlugin : IPlugin
{
    RenderFragment Render() => b => { b.AddContent(0, ""Plugin placeholder""); };
}
