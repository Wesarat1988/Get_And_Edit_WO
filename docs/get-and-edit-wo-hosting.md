# Hosting the Get & Edit Work Orders page

The `GetAndEditWO.UI` Razor Class Library exposes the `/workorders` page that can be plugged into any .NET 8 Razor Components or Blazor Server host.

## 1. Register services

```csharp
using GetAndEditWO.UI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Registers MesOptions and the named MES HttpClient.
builder.Services.AddGetAndEditWo(builder.Configuration);
```

If your app uses the new Razor Components hosting model, call `AddRazorComponents().AddInteractiveServerComponents()` as usual _before_ invoking `AddGetAndEditWo`.

## 2. Provide configuration

Add the `GetAndEditWo` section to your `appsettings.json` (store real values in [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or another secure provider):

```json
"GetAndEditWo": {
  "BaseUrl": "https://mes.example.com/",
  "TokenId": "",
  "SecretKey": ""
}
```

`BaseUrl` must be the MES root URL (for example `https://mes.example.com:10101/`). The `TokenId` and `SecretKey` are supplied at runtime and never committed to source control.

## 3. Make the `/workorders` page discoverable

Ensure the router looks into the RCL assembly. For example:

```razor
@using GetAndEditWO.UI

<Router AppAssembly="@typeof(App).Assembly"
        AdditionalAssemblies="new[] { typeof(WorkOrders).Assembly }">
    ...
</Router>
```

With the service registration and configuration in place, navigating to `/workorders` renders the reusable UI that communicates with MES through the named `HttpClient` (`MES`) and the options supplied by the host.
